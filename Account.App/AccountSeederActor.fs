[<RequireQualifiedAccess>]
module AccountSeederActor

// Actor runs in local development or staging environments to ensure
// some data is available to interact with.

open System
open Akka.Hosting
open Akka.Cluster
open Akkling
open FSharp.Control
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Bank.Account.Api
open Bank.Account.Domain
open ActorUtil

type Status =
   | AwaitingClusterUp
   | AwaitingVerification
   | FinishedSeeding

type CreateAccountsMap = Map<Guid, CreateAccountCommand>

type State = {
   Status: Status
   AccountsToCreate: CreateAccountsMap
}

// Creates a new Map consisting of initial state of accounts to create
// minus accounts created thus far.
let rec getRemainingAccountsToCreate
   (createdAccounts: AccountState list)
   (accountsToCreate: CreateAccountsMap)
   : CreateAccountsMap
   =
   match createdAccounts with
   | [] -> accountsToCreate
   | [ account ] -> Map.remove account.EntityId accountsToCreate
   | account :: rest ->
      getRemainingAccountsToCreate rest
      <| Map.remove account.EntityId accountsToCreate

let private scheduleVerification
   (ctx: Actor<AccountSeederMessage>)
   (seconds: float)
   =
   ctx.Schedule
      (TimeSpan.FromSeconds seconds)
      ctx.Self
      AccountSeederMessage.VerifyAccountsCreated
   |> ignore

let private scheduleInitialization (ctx: Actor<AccountSeederMessage>) =
   ctx.Schedule
      (TimeSpan.FromSeconds 2.)
      ctx.Self
      AccountSeederMessage.SeedAccounts
   |> ignore

let private getVerifiedAccounts (accountsToCreate: CreateAccountsMap) = async {
   let! lookupResults =
      accountsToCreate.Keys |> List.ofSeq |> getAccountsByIds |> Async.ofTask

   return
      lookupResults
      |> Result.toOption
      |> Option.flatten
      |> Option.defaultValue []
}

let private initState = {
   Status = AwaitingClusterUp
   AccountsToCreate =
      Map [
         Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4"),
         CreateAccountCommand.create
            (Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4"))
            {
               FirstName = "Jelly"
               LastName = "Fish"
               Balance = 1300m
               Email = "jellyfish@gmail.com"
               Currency = Currency.USD
            }

         Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5"),
         CreateAccountCommand.create
            (Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5"))
            {
               FirstName = "Star"
               LastName = "Fish"
               Balance = 1000m
               Email = "starfish@gmail.com"
               Currency = Currency.USD
            }

         Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a6"),
         CreateAccountCommand.create
            (Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a6"))
            {
               FirstName = "Rainbow"
               LastName = "Trout"
               Balance = 850m
               Email = "rainbowtrout@gmail.com"
               Currency = Currency.USD
            }
      ]
}

let actorProps (getAccountRef: EntityRefGetter<AccountMessage>) =
   let handler (ctx: Actor<AccountSeederMessage>) =
      let logInfo = logInfo ctx

      let rec loop (state: State) = actor {
         let! msg = ctx.Receive()

         match msg with
         | SeedAccounts ->
            if state.Status <> AwaitingClusterUp then
               logInfo "Account seed already initialized"
            else if not (Cluster.Get(ctx.System).IsUp) then
               logInfo "Cluster not up yet.  Retry account seeding momentarily."
               scheduleInitialization ctx
            else
               let! verified = getVerifiedAccounts state.AccountsToCreate

               if verified.Length = state.AccountsToCreate.Count then
                  return!
                     loop {
                        Status = FinishedSeeding
                        AccountsToCreate = Map.empty
                     }
               else
                  logInfo "Seed postgres with accounts"

                  let remaining =
                     getRemainingAccountsToCreate
                        verified
                        state.AccountsToCreate

                  for command in remaining.Values do
                     getAccountRef command.EntityId
                     <! (StateChange << CreateAccount) command

                  scheduleVerification ctx 5.

                  return!
                     loop {
                        state with
                           Status = AwaitingVerification
                     }
         | VerifyAccountsCreated ->
            let! verified = getVerifiedAccounts state.AccountsToCreate

            if verified.Length = state.AccountsToCreate.Count then
               logInfo "Finished seeding accounts"

               return!
                  loop {
                     Status = FinishedSeeding
                     AccountsToCreate = Map.empty
                  }
            else
               let remaining =
                  getRemainingAccountsToCreate verified state.AccountsToCreate

               logInfo $"{remaining.Count} accounts need to finish seeding"

               scheduleVerification ctx 3.

               return!
                  loop {
                     Status = AwaitingVerification
                     AccountsToCreate = remaining
                  }
      }

      loop initState

   props handler

let get (registry: IActorRegistry) : IActorRef<AccountSeederMessage> =
   typed <| registry.Get<ActorMetadata.AccountSeederMarker>()

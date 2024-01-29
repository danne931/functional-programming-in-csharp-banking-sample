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

open Lib.Types
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
      match lookupResults with
      | Error e -> AccountSeederMessage.ErrorVerifyingAccounts e
      | Ok opt ->
         match opt with
         | None -> AccountSeederMessage.VerifiedAccountsReceived []
         | Some res -> AccountSeederMessage.VerifiedAccountsReceived res
}

let private initState = {
   Status = AwaitingClusterUp
   AccountsToCreate =
      Map [
         Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4"),
         CreateAccountCommand(
            entityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4"),
            firstName = "Jelly",
            lastName = "Fish",
            balance = 1300,
            email = "jellyfish@gmail.com",
            currency = Currency.USD,
            correlationId = Guid.NewGuid()
         )

         Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5"),
         CreateAccountCommand(
            entityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5"),
            firstName = "Star",
            lastName = "Fish",
            balance = 1000,
            email = "starfish@gmail.com",
            currency = Currency.USD,
            correlationId = Guid.NewGuid()
         )

         Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a6"),
         CreateAccountCommand(
            entityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a6"),
            firstName = "Rainbow",
            lastName = "Trout",
            balance = 850,
            email = "rainbowtrout@gmail.com",
            currency = Currency.USD,
            correlationId = Guid.NewGuid()
         )
      ]
}

let actorProps (getAccountRef: EntityRefGetter<AccountMessage>) =
   let handler (ctx: Actor<AccountSeederMessage>) =
      let logInfo = logInfo ctx

      let rec loop (state: State) =
         function
         | SeedAccounts ->
            if state.Status <> AwaitingClusterUp then
               logInfo "Account seed already initialized"
               ignored ()
            else if not (Cluster.Get(ctx.System).IsUp) then
               logInfo "Cluster not up yet.  Retry account seeding momentarily."
               scheduleInitialization ctx
               ignored ()
            else
               logInfo "Seed postgres with accounts"

               for command in state.AccountsToCreate.Values do
                  getAccountRef command.EntityId
                  <! (StateChange << CreateAccount) command

               scheduleVerification ctx 5.

               become
               <| loop {
                  state with
                     Status = AwaitingVerification
               }
         | VerifyAccountsCreated ->
            getVerifiedAccounts state.AccountsToCreate |!> ctx.Self

            ignored ()
         | VerifiedAccountsReceived verified ->
            if verified.Length = state.AccountsToCreate.Count then
               logInfo "Finished seeding accounts"

               become
               <| loop {
                  Status = FinishedSeeding
                  AccountsToCreate = Map.empty
               }
            else
               let remaining =
                  getRemainingAccountsToCreate verified state.AccountsToCreate

               logInfo $"{remaining.Count} accounts need to finish seeding"

               scheduleVerification ctx 3.

               become
               <| loop {
                  Status = AwaitingVerification
                  AccountsToCreate = remaining
               }
         | ErrorVerifyingAccounts err ->
            logError ctx $"Error verifying accounts {err}"
            scheduleVerification ctx 7.
            ignored ()

      loop initState

   props <| actorOf2 handler

let get (registry: IActorRegistry) : IActorRef<AccountSeederMessage> =
   typed <| registry.Get<ActorMetadata.AccountSeederMarker>()

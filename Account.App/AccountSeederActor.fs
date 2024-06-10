[<RequireQualifiedAccess>]
module AccountSeederActor

// Actor runs in local development or staging environments to ensure
// some data is available to interact with.

open System
open System.Threading.Tasks
open Akka.Hosting
open Akka.Cluster
open Akkling
open Akkling.Cluster.Sharding
open FSharp.Control

open Lib.SharedTypes
open Lib.Postgres
open OrganizationSqlMapper
open Bank.Account.Api
open Bank.Account.Domain
open Bank.Transfer.Domain
open ActorUtil

type Status =
   | AwaitingClusterUp
   | AwaitingVerification
   | FinishedSeeding

type CreateAccountsMap = Map<AccountId, CreateAccountCommand>

type State = {
   Status: Status
   AccountsToCreate: CreateAccountsMap
   AccountsToSeedWithTransactions: Map<AccountId, bool>
}

// TODO: Temporarily create org here until fleshed out
let orgId = ORG_ID_REMOVE_SOON

let createOrg () =
   let query =
      $"""
      INSERT into {OrganizationSqlMapper.table} ({OrgFields.orgId}, {OrgFields.name})
      VALUES (@orgId, @name)
      ON CONFLICT ({OrgFields.name})
      DO NOTHING;
      """

   pgPersist query [
      "orgId", OrgSqlWriter.orgId orgId
      "name", OrgSqlWriter.name "test-org"
   ]

let mockAccounts =
   let withModifiedTimestamp (command: CreateAccountCommand) = {
      command with
         Timestamp = command.Timestamp.AddMonths -1
   }

   let cmd1 =
      CreateAccountCommand.create {
         FirstName = "Jelly"
         LastName = "Fish"
         Email = "jellyfish@gmail.com"
         Currency = Currency.USD
         AccountId =
            "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4" |> Guid.Parse |> AccountId
         OrgId = orgId
      }
      |> withModifiedTimestamp

   let cmd2 =
      CreateAccountCommand.create {
         FirstName = "Star"
         LastName = "Fish"
         Email = "starfish@gmail.com"
         Currency = Currency.USD
         AccountId =
            "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5" |> Guid.Parse |> AccountId
         OrgId = orgId
      }
      |> withModifiedTimestamp

   let cmd3 =
      CreateAccountCommand.create {
         FirstName = "Rainbow"
         LastName = "Trout"
         Email = "rainbowtrout@gmail.com"
         Currency = Currency.USD
         AccountId =
            "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a6" |> Guid.Parse |> AccountId
         OrgId = orgId
      }
      |> withModifiedTimestamp

   Map [
      cmd1.Data.AccountId, cmd1

      cmd2.Data.AccountId, cmd2

      cmd3.Data.AccountId, cmd3
   ]

let seedAccountTransactions
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (command: CreateAccountCommand)
   =
   task {
      let accountId = command.Data.AccountId
      let compositeId = accountId, command.OrgId
      let aref = getAccountRef accountId

      let rnd = new Random()

      let randomAmount () =
         decimal (rnd.Next(13, 42)) + decimal (rnd.NextDouble())

      let txnOrigins = [
         "Trader Joe's"
         "Nem nướng Happy Belly"
         "Cơm Chay All Day"
         "Thai Spice ++"
         "Pho Number 1"
      ]

      let timestamp = command.Timestamp.AddDays(rnd.Next(1, 3))

      let command = {
         DepositCashCommand.create compositeId {
            Amount = 1500m + randomAmount ()
            Origin = None
         } with
            Timestamp = timestamp
      }

      aref <! (StateChange << DepositCash) command

      for num in [ 1..3 ] do
         let timestamp = timestamp.AddDays((double num) * 2.3)

         let command = {
            DebitCommand.create compositeId {
               Date = timestamp
               Amount = randomAmount ()
               Origin = txnOrigins[num]
               Reference = None
            } with
               Timestamp = timestamp
         }

         aref <! (StateChange << Debit) command

      let command =
         StartBillingCycleCommand.create compositeId { Reference = None }

      aref <! (StateChange << StartBillingCycle) command

      do! Task.Delay 1300

      for num in [ 1..7 ] do
         let command =
            DebitCommand.create compositeId {
               Date = DateTime.UtcNow
               Amount = randomAmount ()
               Origin = txnOrigins[rnd.Next(0, txnOrigins.Length - 1)]
               Reference = None
            }

         aref <! (StateChange << Debit) command

         if num = 2 || num = 5 then
            let command =
               DepositCashCommand.create compositeId {
                  Amount = randomAmount ()
                  Origin = None
               }

            aref <! (StateChange << DepositCash) command

      if
         (AccountId.fromEntityId command.EntityId) = Seq.head mockAccounts.Keys
      then
         let lockCmd = LockCardCommand.create compositeId { Reference = None }
         aref <! (StateChange << LockCard) lockCmd

         let unlockCmd =
            UnlockCardCommand.create compositeId { Reference = None }

         aref <! (StateChange << UnlockCard) unlockCmd

         for num in [ 1..2 ] do
            let createAccountCmd = mockAccounts.Values |> Seq.item num

            let registerRecipientCmd =
               RegisterInternalTransferRecipientCommand.create compositeId {
                  AccountId = createAccountCmd.Data.AccountId
                  LastName = createAccountCmd.Data.LastName
                  FirstName = createAccountCmd.Data.FirstName
               }

            aref
            <! (StateChange << RegisterInternalTransferRecipient)
                  registerRecipientCmd

            let transferCmd =
               InternalTransferCommand.create compositeId {
                  RecipientId = registerRecipientCmd.Data.AccountId
                  Amount = randomAmount ()
                  ScheduledDate = DateTime.UtcNow
                  Memo = None
               }

            aref <! (StateChange << InternalTransfer) transferCmd
   }

// Creates a new Map consisting of initial state of accounts to create
// minus accounts created thus far.
let rec getRemainingAccountsToCreate
   (createdAccounts: Account list)
   (accountsToCreate: CreateAccountsMap)
   : CreateAccountsMap
   =
   match createdAccounts with
   | [] -> accountsToCreate
   | [ account ] -> Map.remove account.AccountId accountsToCreate
   | account :: rest ->
      getRemainingAccountsToCreate rest
      <| Map.remove account.AccountId accountsToCreate

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
   AccountsToCreate = mockAccounts
   AccountsToSeedWithTransactions =
      Map [ for acctId in mockAccounts.Keys -> acctId, true ]
}

let actorProps (getAccountRef: AccountId -> IEntityRef<AccountMessage>) =
   let handler (ctx: Actor<AccountSeederMessage>) =
      let logInfo = logInfo ctx

      let rec loop (state: State) = actor {
         let! msg = ctx.Receive()

         match msg with
         | AccountSeederMessage.SeedAccounts ->
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
                        state with
                           Status = FinishedSeeding
                           AccountsToCreate = Map.empty
                     }
               else
                  logInfo "Seed postgres with accounts"

                  match! createOrg () with
                  | Error err ->
                     logError
                        ctx
                        $"Seed accounts: Error creating organization. {err}"

                     scheduleInitialization ctx
                     return! loop state
                  | Ok _ ->
                     let remaining =
                        getRemainingAccountsToCreate
                           verified
                           state.AccountsToCreate

                     for command in remaining.Values do
                        let aref = getAccountRef command.Data.AccountId
                        aref <! (StateChange << CreateAccount) command

                     scheduleVerification ctx 5.

                     return!
                        loop {
                           state with
                              Status = AwaitingVerification
                        }
         | AccountSeederMessage.VerifyAccountsCreated ->
            let! verified = getVerifiedAccounts state.AccountsToCreate

            let accountsToSeedWithTxns, seeded =
               verified
               |> List.partition (fun account ->
                  state.AccountsToSeedWithTransactions[account.AccountId])

            for acct in accountsToSeedWithTxns do
               do!
                  seedAccountTransactions
                     getAccountRef
                     state.AccountsToCreate[acct.AccountId]

               ()

            let txnsSeedMap =
               accountsToSeedWithTxns @ seeded
               |> List.fold
                     (fun acc account -> acc |> Map.add account.AccountId false)
                     state.AccountsToSeedWithTransactions

            if verified.Length = state.AccountsToCreate.Count then
               logInfo "Finished seeding accounts"

               return!
                  loop {
                     Status = FinishedSeeding
                     AccountsToCreate = Map.empty
                     AccountsToSeedWithTransactions = txnsSeedMap
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
                     AccountsToSeedWithTransactions = txnsSeedMap
                  }
      }

      loop initState

   props handler

let get (registry: IActorRegistry) : IActorRef<AccountSeederMessage> =
   typed <| registry.Get<ActorMetadata.AccountSeederMarker>()

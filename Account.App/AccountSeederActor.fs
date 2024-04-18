[<RequireQualifiedAccess>]
module AccountSeederActor

// Actor runs in local development or staging environments to ensure
// some data is available to interact with.

open System
open System.Threading.Tasks
open Akka.Hosting
open Akka.Cluster
open Akkling
open FSharp.Control
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Bank.Account.Api
open Bank.Account.Domain
open Bank.Transfer.Domain
open ActorUtil

type Status =
   | AwaitingClusterUp
   | AwaitingVerification
   | FinishedSeeding

type CreateAccountsMap = Map<Guid, CreateAccountCommand>

type State = {
   Status: Status
   AccountsToCreate: CreateAccountsMap
   AccountsToSeedWithTransactions: Map<Guid, bool>
}

let mockAccounts =
   let withModifiedTimestamp (command: CreateAccountCommand) = {
      command with
         Timestamp = command.Timestamp.AddMonths -1
   }

   let account1 =
      CreateAccountCommand.create (Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4")) {
         FirstName = "Jelly"
         LastName = "Fish"
         Balance = 1300m
         Email = "jellyfish@gmail.com"
         Currency = Currency.USD
      }
      |> withModifiedTimestamp

   let account2 =
      CreateAccountCommand.create (Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5")) {
         FirstName = "Star"
         LastName = "Fish"
         Balance = 1000m
         Email = "starfish@gmail.com"
         Currency = Currency.USD
      }
      |> withModifiedTimestamp

   let account3 =
      CreateAccountCommand.create (Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a6")) {
         FirstName = "Rainbow"
         LastName = "Trout"
         Balance = 850m
         Email = "rainbowtrout@gmail.com"
         Currency = Currency.USD
      }
      |> withModifiedTimestamp

   Map [
      account1.EntityId, account1

      account2.EntityId, account2

      account3.EntityId, account3
   ]

let seedAccountTransactions
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (command: CreateAccountCommand)
   =
   task {
      let accountId = command.EntityId
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
         DepositCashCommand.create accountId {
            Date = timestamp
            Amount = randomAmount ()
            Origin = None
         } with
            Timestamp = timestamp
      }

      aref <! (StateChange << DepositCash) command

      for num in [ 1..3 ] do
         let timestamp = timestamp.AddDays((double num) * 2.3)

         let command = {
            DebitCommand.create accountId {
               Date = timestamp
               Amount = randomAmount ()
               Origin = txnOrigins[num]
               Reference = None
            } with
               Timestamp = timestamp
         }

         aref <! (StateChange << Debit) command

      let command =
         StartBillingCycleCommand.create command.EntityId { Reference = None }

      aref <! (StateChange << StartBillingCycle) command

      do! Task.Delay 1300

      for num in [ 1..7 ] do
         let command =
            DebitCommand.create accountId {
               Date = DateTime.UtcNow
               Amount = randomAmount ()
               Origin = txnOrigins[rnd.Next(0, txnOrigins.Length - 1)]
               Reference = None
            }

         aref <! (StateChange << Debit) command

         if num = 2 || num = 5 then
            let command =
               DepositCashCommand.create accountId {
                  Date = timestamp
                  Amount = randomAmount ()
                  Origin = None
               }

            aref <! (StateChange << DepositCash) command

      if accountId = Seq.head mockAccounts.Keys then
         let lockCmd = LockCardCommand.create accountId { Reference = None }
         aref <! (StateChange << LockCard) lockCmd

         let unlockCmd = UnlockCardCommand.create accountId { Reference = None }
         aref <! (StateChange << UnlockCard) unlockCmd

         for num in [ 1..2 ] do
            let createAccountCmd = mockAccounts.Values |> Seq.item num
            let recipientId = createAccountCmd.EntityId

            let registerRecipientCmd =
               RegisterTransferRecipientCommand.create accountId {
                  Recipient = {
                     LastName = createAccountCmd.Data.LastName
                     FirstName = createAccountCmd.Data.FirstName
                     AccountEnvironment = RecipientAccountEnvironment.Internal
                     Identification = string recipientId
                     IdentificationStrategy =
                        RecipientAccountIdentificationStrategy.AccountId
                     RoutingNumber = None
                     Status = RecipientRegistrationStatus.Confirmed
                  }
               }

            aref
            <! (StateChange << RegisterTransferRecipient) registerRecipientCmd

            let transferCmd =
               TransferCommand.create accountId {
                  Recipient = registerRecipientCmd.Data.Recipient
                  Amount = randomAmount ()
                  Date = DateTime.UtcNow
                  Reference = None
               }

            aref <! (StateChange << Transfer) transferCmd

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
   AccountsToCreate = mockAccounts
   AccountsToSeedWithTransactions =
      Map [ for acctId in mockAccounts.Keys -> acctId, true ]
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
                        state with
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
                     let aref = getAccountRef command.EntityId
                     aref <! (StateChange << CreateAccount) command

                  scheduleVerification ctx 5.

                  return!
                     loop {
                        state with
                           Status = AwaitingVerification
                     }
         | VerifyAccountsCreated ->
            let! verified = getVerifiedAccounts state.AccountsToCreate

            let accountsToSeedWithTxns, seeded =
               verified
               |> List.partition (fun account ->
                  state.AccountsToSeedWithTransactions[account.EntityId])

            for acct in accountsToSeedWithTxns do
               do!
                  seedAccountTransactions
                     getAccountRef
                     state.AccountsToCreate[acct.EntityId]

               ()

            let txnsSeedMap =
               accountsToSeedWithTxns @ seeded
               |> List.fold
                     (fun acc account -> acc |> Map.add account.EntityId false)
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

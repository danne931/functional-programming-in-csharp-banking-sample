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
open Bank.Employee.Domain
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
      INSERT into {OrganizationSqlMapper.table} (
         {OrgFields.orgId},
         {OrgFields.name},
         {OrgFields.requiresEmployeeInviteApproval}
      )
      VALUES (@orgId, @name, @requiresEmployeeInviteApproval)
      ON CONFLICT ({OrgFields.name})
      DO NOTHING;
      """

   pgPersist query [
      "orgId", OrgSqlWriter.orgId orgId
      "name", OrgSqlWriter.name "test-org"
      "requiresEmployeeInviteApproval",
      OrgSqlWriter.requiresEmployeeInviteApproval false
   ]

let mockAccountOwnerCmd =
   let withModifiedTimestamp (command: CreateAccountOwnerCommand) = {
      command with
         Timestamp = command.Timestamp.AddMonths -1
   }

   CreateAccountOwnerCommand.create {
      Email = "jellyfish@gmail.com"
      FirstName = "Daniel"
      LastName = "Eisenbarger"
      OrgId = orgId
   }
   |> withModifiedTimestamp

let mockAccountOwnerId = mockAccountOwnerCmd.InitiatedBy

let mockEmployees =
   let withModifiedTimestamp (command: CreateEmployeeCommand) = {
      command with
         Timestamp = command.Timestamp.AddMonths -1
   }

   let cmd1 =
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "starfish@gmail.com"
         FirstName = "Star"
         LastName = "Fish"
         OrgId = orgId
         Role = Role.Admin
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      }
      |> withModifiedTimestamp

   Map [ EmployeeId.fromEntityId cmd1.EntityId, cmd1 ]


let mockAccounts =
   let withModifiedTimestamp (command: CreateAccountCommand) = {
      command with
         Timestamp = command.Timestamp.AddMonths -1
   }

   let cmd1 =
      CreateAccountCommand.create {
         Name = "AR"
         Currency = Currency.USD
         AccountId =
            "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4" |> Guid.Parse |> AccountId
         OrgId = orgId
         InitiatedBy = mockAccountOwnerId
      }
      |> withModifiedTimestamp

   let cmd2 =
      CreateAccountCommand.create {
         Name = "AP"
         Currency = Currency.USD
         AccountId =
            "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5" |> Guid.Parse |> AccountId
         OrgId = orgId
         InitiatedBy = mockAccountOwnerId
      }
      |> withModifiedTimestamp

   let cmd3 =
      CreateAccountCommand.create {
         Name = "Operations"
         Currency = Currency.USD
         AccountId =
            "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a6" |> Guid.Parse |> AccountId
         OrgId = orgId
         InitiatedBy = mockAccountOwnerId
      }
      |> withModifiedTimestamp

   Map [
      cmd1.Data.AccountId, cmd1

      cmd2.Data.AccountId, cmd2

      cmd3.Data.AccountId, cmd3
   ]

let seedAccountTransactions
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   (command: CreateAccountCommand)
   =
   task {
      let accountId = command.Data.AccountId
      let compositeId = accountId, command.OrgId
      let accountRef = getAccountRef accountId

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

      let msg =
         {
            DepositCashCommand.create compositeId mockAccountOwnerId {
               Amount = 1500m + randomAmount ()
               Origin = None
            } with
               Timestamp = timestamp
         }
         |> AccountCommand.DepositCash
         |> AccountMessage.StateChange

      accountRef <! msg

      (*
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
      *)

      let msg =
         StartBillingCycleCommand.create compositeId { Reference = None }
         |> AccountCommand.StartBillingCycle
         |> AccountMessage.StateChange

      accountRef <! msg

      do! Task.Delay 1300

      if accountId = Seq.head mockAccounts.Keys then
         let employeeCreateCmd = mockEmployees.Head().Value
         let employeeId = EmployeeId.fromEntityId employeeCreateCmd.EntityId
         let employeeRef = getEmployeeRef employeeId

         let msg =
            employeeCreateCmd
            |> EmployeeCommand.CreateEmployee
            |> EmployeeMessage.StateChange

         employeeRef <! msg

         let msg =
            ConfirmInvitationCommand.create (employeeId, orgId) {
               Email = Email.deserialize employeeCreateCmd.Data.Email
               AuthProviderUserId = Guid.NewGuid()
               Reference = None
            }
            |> EmployeeCommand.ConfirmInvitation
            |> EmployeeMessage.StateChange

         employeeRef <! msg

         do! Task.Delay 7000

         let createCardCmd =
            CreateCardCommand.create {
               CardId = Guid.NewGuid() |> CardId
               EmployeeId = employeeId
               OrgId = orgId
               AccountId = accountId
               PersonName =
                  $"{employeeCreateCmd.Data.FirstName} {employeeCreateCmd.Data.LastName}"
               CardNickname = Some "Lunch"
               CardType = CardType.Debit
               Virtual = true
               DailyPurchaseLimit = None
               MonthlyPurchaseLimit = None
               InitiatedBy = mockAccountOwnerId
            }

         let msg =
            createCardCmd
            |> EmployeeCommand.CreateCard
            |> EmployeeMessage.StateChange

         employeeRef <! msg

         for num in [ 1..7 ] do
            let msg =
               DebitRequestCommand.create (employeeId, orgId) {
                  AccountId = accountId
                  CardId = createCardCmd.Data.CardId
                  CardNumberLast4 = "1234"
                  Date = DateTime.UtcNow
                  Amount = randomAmount ()
                  Origin = txnOrigins[rnd.Next(0, txnOrigins.Length - 1)]
                  Reference = None
               }
               |> EmployeeCommand.DebitRequest
               |> EmployeeMessage.StateChange

            employeeRef <! msg

            if num = 2 || num = 5 then
               let msg =
                  DepositCashCommand.create compositeId mockAccountOwnerId {
                     Amount = randomAmount ()
                     Origin = None
                  }
                  |> AccountCommand.DepositCash
                  |> AccountMessage.StateChange

               accountRef <! msg

         for num in [ 1..2 ] do
            let createAccountCmd = mockAccounts.Values |> Seq.item num

            let internalRecipientCmd =
               RegisterInternalTransferRecipientCommand.create
                  compositeId
                  mockAccountOwnerId
                  {
                     AccountId = createAccountCmd.Data.AccountId
                     Name = createAccountCmd.Data.Name
                  }

            let msg =
               internalRecipientCmd
               |> AccountCommand.RegisterInternalTransferRecipient
               |> AccountMessage.StateChange

            accountRef <! msg

            let msg =
               InternalTransferCommand.create compositeId mockAccountOwnerId {
                  BaseInfo = {
                     RecipientId = internalRecipientCmd.Data.AccountId
                     Amount = randomAmount ()
                     ScheduledDate = DateTime.UtcNow
                  }
                  Memo = None
               }
               |> AccountCommand.InternalTransfer
               |> AccountMessage.StateChange

            accountRef <! msg
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

let actorProps
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   =
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
                        if
                           command.Data.AccountId = Seq.head mockAccounts.Keys
                        then
                           let employeeId =
                              EmployeeId.fromEntityId
                                 mockAccountOwnerCmd.EntityId

                           let cmd = mockAccountOwnerCmd

                           let createMsg =
                              cmd
                              |> EmployeeCommand.CreateAccountOwner
                              |> EmployeeMessage.StateChange

                           let confirmInviteCmd =
                              ConfirmInvitationCommand.create
                                 (employeeId, cmd.OrgId)
                                 {
                                    Email = Email.deserialize cmd.Data.Email
                                    Reference = None
                                    AuthProviderUserId = Guid.NewGuid()
                                 }
                              |> EmployeeCommand.ConfirmInvitation
                              |> EmployeeMessage.StateChange

                           let employeeRef = getEmployeeRef employeeId
                           employeeRef <! createMsg
                           employeeRef <! confirmInviteCmd

                        let accountRef = getAccountRef command.Data.AccountId

                        let msg =
                           command
                           |> AccountCommand.CreateAccount
                           |> AccountMessage.StateChange

                        accountRef <! msg

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
                     getEmployeeRef
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

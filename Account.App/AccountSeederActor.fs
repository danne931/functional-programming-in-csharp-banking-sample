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
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Lib.Postgres
open OrganizationSqlMapper
open EmployeeEventSqlMapper
open TransactionSqlMapper
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
let orgId = Constants.ORG_ID_REMOVE_SOON

let socialTransferCandidates =
   [ "Linear"; "Figma"; "Lendtable"; "Shopify" ]
   |> List.map (fun name ->
      let orgId = Guid.NewGuid() |> OrgId

      orgId,
      {|
         OrgId = orgId
         AccountOwner = Guid.NewGuid() |> EmployeeId
         PrimaryAccountId = Guid.NewGuid() |> AccountId
         Name = name
      |})
   |> Map.ofList

let createOrgs () =
   let orgs =
      (orgId, "Sapphire Optics")
      :: (socialTransferCandidates.Values
          |> Seq.map (fun o -> o.OrgId, o.Name)
          |> List.ofSeq)

   pgTransaction [
      $"""
      INSERT into {OrganizationSqlMapper.table} (
         {OrgFields.orgId},
         {OrgFields.name}
      )
      VALUES (@orgId, @name);
      """,
      orgs
      |> List.map (fun (orgId, name) -> [
         "orgId", OrgSqlWriter.orgId orgId
         "name", OrgSqlWriter.name name
      ])

      $"""
      INSERT into {OrganizationSqlMapper.permissionsTable} (
         {OrgFields.orgId},
         {OrgFields.requiresEmployeeInviteApproval}
      )
      VALUES (@orgId, @requiresEmployeeInviteApproval);
      """,
      orgs
      |> List.map (fun (orgId, _) -> [
         "orgId", OrgSqlWriter.orgId orgId
         "requiresEmployeeInviteApproval",
         OrgSqlWriter.requiresEmployeeInviteApproval false
      ])
   ]

let enableOrgSocialTransferDiscovery () =
   let query =
      $"""
      WITH updates AS (
         SELECT
            unnest(@orgIds) AS org,
            unnest(@accountIds) AS account
      )
      UPDATE {OrganizationSqlMapper.permissionsTable} op
      SET {OrgFields.socialTransferDiscoveryAccountId} = u.account
      FROM updates u
      WHERE op.{OrgFields.orgId} = u.org;
      """

   let orgIds =
      socialTransferCandidates |> Map.toArray |> Array.map (fst >> OrgId.get)

   let accountIds =
      socialTransferCandidates
      |> Map.toArray
      |> Array.map (snd >> _.PrimaryAccountId >> AccountId.get)

   pgPersist query [
      "orgIds", Sql.uuidArray orgIds
      "accountIds", Sql.uuidArray accountIds
   ]

// NOTE:
// Initial employee purchase requests are configured with timestamps
// in the past.  However, subsequent employee purchase approval/decline
// in the employee_event table as well as recording of debits in the
// account transaction table have current timestamps since we
// don't have control over the creation of system-produced events.
// Modifying system-produced event timestamps via update statements on
// the read models as demonstrated below should be a sufficient strategy
// for observing time based analytics on seed data.
let seedBalanceHistory () = taskResultOption {
   let query =
      $"SELECT {EmployeeEventFields.timestamp}, {EmployeeEventFields.correlationId}
        FROM {EmployeeEventSqlMapper.table}
        WHERE {EmployeeEventFields.name} = 'DebitRequested'"

   let! employeePurchaseRequests =
      pgQuery query None (fun read -> {|
         CorrelationId = EmployeeEventSqlReader.correlationId read
         Timestamp = EmployeeEventSqlReader.timestamp read
      |})

   let sqlParams =
      employeePurchaseRequests
      |> List.map (fun o -> [
         "correlationId", EmployeeEventSqlWriter.correlationId o.CorrelationId
         "timestamp",
         EmployeeEventSqlWriter.timestamp (o.Timestamp.AddSeconds 3)
      ])

   let query = [
      $"""
      UPDATE {EmployeeEventSqlMapper.table}
      SET
         {EmployeeEventFields.timestamp} = @timestamp,
         {EmployeeEventFields.event} = jsonb_set(
            {EmployeeEventFields.event},
            '{{1,Timestamp}}',
            to_jsonb(@timestamp),
            false
         )
      WHERE
         {EmployeeEventFields.correlationId} = @correlationId
         AND {EmployeeEventFields.name} <> 'DebitRequested';
      """,
      sqlParams

      $"""
      UPDATE {TransactionSqlMapper.table}
      SET
         {TransactionFields.timestamp} = @timestamp,
         {TransactionFields.event} = jsonb_set(
            {TransactionFields.event},
            '{{1,Timestamp}}',
            to_jsonb(@timestamp),
            false
         )
      WHERE
         {TransactionFields.correlationId} = @correlationId
         AND {TransactionFields.name} = 'DebitedAccount';
      """,
      sqlParams
   ]

   let! _ = pgTransaction query |> TaskResult.map Some
   let! res = pgProcedure "seed_balance_history" None |> TaskResult.map Some
   return res
}

let mockAccountOwnerCmd =
   let date = DateTime.Today
   let startOfMonth = DateTime(date.Year, date.Month, 1).ToUniversalTime()

   {
      CreateAccountOwnerCommand.create {
         Email = "jellyfish@gmail.com"
         FirstName = "Daniel"
         LastName = "Eisenbarger"
         OrgId = orgId
      } with
         Timestamp = startOfMonth.AddMonths -3
   }

let mockAccountOwnerId = mockAccountOwnerCmd.InitiatedBy

let arCheckingAccountId =
   "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4" |> Guid.Parse |> AccountId

let apCheckingAccountId =
   "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5" |> Guid.Parse |> AccountId

let opsCheckingAccountId =
   "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a6" |> Guid.Parse |> AccountId

let mockAccounts =
   let withModifiedTimestamp (command: CreateAccountCommand) = {
      command with
         Timestamp = mockAccountOwnerCmd.Timestamp
   }

   let cmd1 =
      CreateAccountCommand.create {
         Name = "AR"
         Currency = Currency.USD
         Depository = AccountDepository.Checking
         AccountId = arCheckingAccountId
         AccountNumber = AccountNumber.generate ()
         OrgId = orgId
         InitiatedBy = mockAccountOwnerId
      }
      |> withModifiedTimestamp

   let cmd2 =
      CreateAccountCommand.create {
         Name = "AP"
         Currency = Currency.USD
         Depository = AccountDepository.Checking
         AccountId = apCheckingAccountId
         AccountNumber = AccountNumber.generate ()
         OrgId = orgId
         InitiatedBy = mockAccountOwnerId
      }
      |> withModifiedTimestamp

   let cmd3 =
      CreateAccountCommand.create {
         Name = "Operations"
         Currency = Currency.USD
         Depository = AccountDepository.Checking
         AccountId = opsCheckingAccountId
         AccountNumber = AccountNumber.generate ()
         OrgId = orgId
         InitiatedBy = mockAccountOwnerId
      }
      |> withModifiedTimestamp

   let socialTransferCandidates =
      socialTransferCandidates
      |> Map.fold
            (fun acc _ o ->
               let cmd =
                  CreateAccountCommand.create {
                     Name = "AR"
                     Currency = Currency.USD
                     Depository = AccountDepository.Checking
                     AccountId = o.PrimaryAccountId
                     AccountNumber = AccountNumber.generate ()
                     OrgId = o.OrgId
                     InitiatedBy = InitiatedById o.AccountOwner
                  }
                  |> withModifiedTimestamp

               Map.add o.PrimaryAccountId cmd acc)
            Map.empty
      |> Map.toSeq

   Map [
      cmd1.Data.AccountId, cmd1

      cmd2.Data.AccountId, cmd2

      cmd3.Data.AccountId, cmd3

      yield! socialTransferCandidates
   ]

let accountInitialDeposits =
   Map [ arCheckingAccountId, 2_500_931m; opsCheckingAccountId, 1_391_100m ]

let mockAccountOwnerCards =
   let cmd = mockAccountOwnerCmd
   let emId = EmployeeId.fromEntityId mockAccountOwnerCmd.EntityId

   let cardCmd1 = {
      CreateCardCommand.create {
         CardId = Guid.NewGuid() |> CardId
         EmployeeId = emId
         OrgId = orgId
         AccountId = arCheckingAccountId
         PersonName = $"{cmd.Data.FirstName} {cmd.Data.LastName}"
         CardNickname = Some "Travel"
         CardType = CardType.Debit
         Virtual = true
         DailyPurchaseLimit = Some 10_000m
         MonthlyPurchaseLimit = None
         InitiatedBy = mockAccountOwnerId
      } with
         Timestamp = cmd.Timestamp.AddHours 1
   }

   let cardCmd2 = {
      cardCmd1 with
         Data.CardId = Guid.NewGuid() |> CardId
         Data.CardNickname = Some "Web Services"
         Data.DailyPurchaseLimit = Some 40_000m
         Timestamp = cmd.Timestamp.AddHours 1.1
   }

   cardCmd1, cardCmd2

let mockEmployees =
   let cmd1 = {
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "starfish@gmail.com"
         FirstName = "Star"
         LastName = "Fish"
         OrgId = orgId
         Role = Role.Admin
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      } with
         Timestamp = mockAccountOwnerCmd.Timestamp.AddHours 5
   }

   let cmd2 = {
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "blowfish@gmail.com"
         FirstName = "Blow"
         LastName = "Fish"
         OrgId = orgId
         Role = Role.CardOnly
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      } with
         Timestamp = mockAccountOwnerCmd.Timestamp.AddDays 1
   }

   let createCard (cmd: CreateEmployeeCommand) = {
      CreateCardCommand.create {
         CardId = Guid.NewGuid() |> CardId
         EmployeeId = EmployeeId.fromEntityId cmd.EntityId
         OrgId = orgId
         AccountId = arCheckingAccountId
         PersonName = $"{cmd.Data.FirstName} {cmd.Data.LastName}"
         CardNickname = Some "Travel"
         CardType = CardType.Debit
         Virtual = true
         DailyPurchaseLimit = Some 10_000m
         MonthlyPurchaseLimit = None
         InitiatedBy = mockAccountOwnerId
      } with
         Timestamp = cmd.Timestamp.AddHours 1
   }

   Map [
      EmployeeId.fromEntityId cmd1.EntityId, (cmd1, createCard cmd1)
      EmployeeId.fromEntityId cmd2.EntityId, (cmd2, createCard cmd2)
   ]

let randomAmount min max =
   let rnd = new Random()
   decimal (rnd.Next(min, max)) + decimal (rnd.NextDouble())

let seedAccountOwnerActions
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (card: Card)
   (employeeId: EmployeeId)
   (timestamp: DateTime)
   =
   for month in [ 1..3 ] do
      let timestamp = timestamp.AddMonths month
      let accountRef = getAccountRef arCheckingAccountId

      let purchaseCmd = {
         DebitRequestCommand.create (employeeId, orgId) {
            AccountId = card.AccountId
            CardId = card.CardId
            CardNumberLast4 = card.CardNumberLast4
            Date = timestamp
            Amount = 30_000m + (randomAmount 1000 7000)
            Origin = "Microsoft Azure"
            Reference = None
         } with
            Timestamp = timestamp
      }

      let msg =
         purchaseCmd
         |> EmployeeCommand.DebitRequest
         |> EmployeeMessage.StateChange

      getEmployeeRef employeeId <! msg

      for num in [ 1..3 ] do
         let maxDays =
            let daysToAdd = num * (5 + num)

            if month = 3 then
               let buffer = DateTime.UtcNow.AddDays(-2).Day
               if buffer / daysToAdd >= 1 then daysToAdd else 0
            else
               daysToAdd

         if maxDays = 0 && num > 1 then
            ()
         else
            let msg =
               {
                  DepositCashCommand.create
                     (arCheckingAccountId, orgId)
                     mockAccountOwnerId
                     {
                        Amount = 5000m + randomAmount 1000 10_000
                        Origin = Some "Deposit"
                     } with
                     Timestamp = timestamp.AddDays(float maxDays)
               }
               |> AccountCommand.DepositCash
               |> AccountMessage.StateChange

            accountRef <! msg

            let ind =
               int (randomAmount 0 (socialTransferCandidates.Length() - 1))

            let recipient = socialTransferCandidates.Values |> Seq.item ind

            let timestamp = timestamp.AddDays(float (maxDays - 1))

            let msg =
               {
                  InternalTransferBetweenOrgsCommand.create
                     (arCheckingAccountId, orgId)
                     mockAccountOwnerId
                     {
                        Memo = None
                        BaseInfo = {
                           RecipientOrgId = recipient.OrgId
                           RecipientId = recipient.PrimaryAccountId
                           RecipientName = recipient.Name
                           Amount = 3000m + randomAmount 1000 8000
                           ScheduledDate = timestamp
                           Sender = {
                              Name = mockAccounts[arCheckingAccountId].Data.Name
                              AccountId = arCheckingAccountId
                              OrgId = orgId
                           }
                        }
                     } with
                     Timestamp = timestamp
               }
               |> AccountCommand.InternalTransferBetweenOrgs
               |> AccountMessage.StateChange

            accountRef <! msg

let seedEmployeeActions
   (card: Card)
   (employee: Employee)
   (employeeRef: IEntityRef<EmployeeMessage>)
   (timestamp: DateTime)
   =
   let purchaseOrigins = [
      [ "Cozy Hotel"; "Trader Joe's"; "In N Out"; "Lyft" ]
      [
         "Barn House BBQ and Beer"
         "Nem nướng Happy Belly"
         "Nhà Lồng Coffee"
         "Cơm Chay All Day"
         "Chickpea Eatery"
         "Pho Number 1"
         "Big C"
         "Grab"
         "Thai Spice ++"
         "ร้าน บ้านไร่ยามเย็น"
         "GoGym"
         "Coffee Window @ 14 Soi 7"
         "ร้านอาหารทับริมธาร"
      ]
      [
         "Lidl"
         "Coop"
         "Carrefour"
         "Baita Resch"
         "Mercato di Mezzo"
         "La Locanda Dei Grulli"
         "Rosso Vivo Pizzeria Verace"
         "CAFFE' GM S.R.L."
         "Pizzeria Via Cassia"
         "Cantina Tramin"
         "La Mora Viola (gelateria artigianale)"
      ]
   ]

   let rnd = new Random()
   let compositeId = employee.EmployeeId, orgId

   for month in [ 1..3 ] do
      let timestamp = timestamp.AddMonths month
      let purchaseOrigins = purchaseOrigins[month - 1]

      let maxPurchases =
         if month = 3 then
            DateTime.UtcNow.Day
         else
            DateTime.DaysInMonth(timestamp.Year, timestamp.Month)

      for purchaseNum in [ 1..maxPurchases ] do
         let maxDays =
            if month = 3 then
               let today = DateTime.UtcNow
               if today.Day = 1 then None else Some(today.AddDays(-2).Day)
            else
               Some(DateTime.DaysInMonth(timestamp.Year, timestamp.Month) - 1)

         let purchaseDate =
            match (month = 3 && purchaseNum > maxPurchases - 1), maxDays with
            | false, Some days -> timestamp.AddDays(float (randomAmount 0 days))
            | false, None -> timestamp
            | true, _ -> DateTime.UtcNow

         let purchaseCmd = {
            DebitRequestCommand.create compositeId {
               AccountId = card.AccountId
               CardId = card.CardId
               CardNumberLast4 = card.CardNumberLast4
               Date = purchaseDate
               Amount = randomAmount 50 333
               Origin = purchaseOrigins[rnd.Next(0, purchaseOrigins.Length)]
               Reference = None
            } with
               Timestamp = purchaseDate
         }

         let msg =
            purchaseCmd
            |> EmployeeCommand.DebitRequest
            |> EmployeeMessage.StateChange

         employeeRef <! msg

let createEmployees
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   =
   for employeeId, (employeeCreateCmd, _) in Map.toSeq mockEmployees do
      let employeeRef = getEmployeeRef employeeId

      let msg =
         employeeCreateCmd
         |> EmployeeCommand.CreateEmployee
         |> EmployeeMessage.StateChange

      employeeRef <! msg

      let confirmInviteCmd = {
         ConfirmInvitationCommand.create (employeeId, orgId) {
            Email = Email.deserialize employeeCreateCmd.Data.Email
            AuthProviderUserId = Guid.NewGuid()
            Reference = None
         } with
            Timestamp = employeeCreateCmd.Timestamp.AddHours 1
      }

      let msg =
         confirmInviteCmd
         |> EmployeeCommand.ConfirmInvitation
         |> EmployeeMessage.StateChange

      employeeRef <! msg

let createEmployeeCards
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   =
   let accountOwnerTravelCardCreateCmd, accountOwnerBusinessCardCreateCmd =
      mockAccountOwnerCards

   let employeeCardCreateCmds =
      mockEmployees.Values |> Seq.toList |> List.map snd

   let cardCreateCmds =
      [ accountOwnerBusinessCardCreateCmd; accountOwnerTravelCardCreateCmd ]
      @ employeeCardCreateCmds

   for cmd in cardCreateCmds do
      let employeeRef = getEmployeeRef (EmployeeId.fromEntityId cmd.EntityId)

      let msg = cmd |> EmployeeCommand.CreateCard |> EmployeeMessage.StateChange

      employeeRef <! msg

   {|
      AccountOwnerTravelCard = accountOwnerTravelCardCreateCmd
      AccountOwnerBusinessCard = accountOwnerBusinessCardCreateCmd
      Employee = employeeCardCreateCmds
   |}

let getEmployeeCardPair
   (employeeRef: IEntityRef<EmployeeMessage>)
   (cardId: CardId)
   =
   async {
      let! (employeeOpt: EmployeeWithEvents option) =
         employeeRef <? EmployeeMessage.GetEmployee

      return option {
         let! employee = employeeOpt
         let em = employee.Info
         let! card = em.Cards.TryFind cardId
         return em, card
      }
   }

let seedAccountTransactions
   (mailbox: Actor<AccountSeederMessage>)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   (command: CreateAccountCommand)
   =
   task {
      let accountId = command.Data.AccountId
      let compositeId = accountId, command.OrgId
      let accountRef = getAccountRef accountId

      let timestamp = command.Timestamp.AddHours 2

      match Map.tryFind accountId accountInitialDeposits with
      | Some depositAmount ->
         let msg =
            {
               DepositCashCommand.create compositeId mockAccountOwnerId {
                  Amount = depositAmount
                  Origin = Some "Deposit"
               } with
                  Timestamp = timestamp
            }
            |> AccountCommand.DepositCash
            |> AccountMessage.StateChange

         accountRef <! msg
      | None -> ()

      if accountId = arCheckingAccountId then
         createEmployees getEmployeeRef
         do! Task.Delay 10_000
         let cardCreateCmds = createEmployeeCards getEmployeeRef
         do! Task.Delay 10_000

         let businessCardCreateCmd = cardCreateCmds.AccountOwnerBusinessCard

         let accountOwnerId =
            EmployeeId.fromEntityId businessCardCreateCmd.EntityId

         let accountOwnerBusinessCardId = businessCardCreateCmd.Data.CardId
         let employeeRef = getEmployeeRef accountOwnerId

         let! accountOwnerCardPairOpt =
            getEmployeeCardPair employeeRef accountOwnerBusinessCardId

         match accountOwnerCardPairOpt with
         | None ->
            logError
               mailbox
               $"Can not proceed with account owner actions - eId: {accountOwnerId} cId: {accountOwnerBusinessCardId}"
         | Some(_, card) ->
            seedAccountOwnerActions
               getEmployeeRef
               getAccountRef
               card
               accountOwnerId
               businessCardCreateCmd.Timestamp

         for cmd in
            cardCreateCmds.AccountOwnerTravelCard :: cardCreateCmds.Employee do
            let employeeId = EmployeeId.fromEntityId cmd.EntityId
            let cardId = cmd.Data.CardId
            let employeeRef = getEmployeeRef employeeId
            let! employeeCardPairOpt = getEmployeeCardPair employeeRef cardId

            match employeeCardPairOpt with
            | None ->
               logError
                  mailbox
                  $"Can not proceed with purchases - eId: {employeeId} cId: {cardId}"
            | Some(employee, card) ->
               seedEmployeeActions card employee employeeRef timestamp
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

                  match! createOrgs () with
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
                        if command.Data.AccountId = arCheckingAccountId then
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
                     ctx
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

               match! enableOrgSocialTransferDiscovery () with
               | Ok _ ->
                  logInfo
                     $"Enabled social transfer discovery for {arCheckingAccountId}"
               | Error err ->
                  logError ctx $"Error enabling social transfer discovery {err}"

               do! Task.Delay 35_000
               let! res = seedBalanceHistory ()

               match res with
               | Ok(Some _) ->
                  logInfo "Seeded balance history"

                  return!
                     loop {
                        Status = FinishedSeeding
                        AccountsToCreate = Map.empty
                        AccountsToSeedWithTransactions = txnsSeedMap
                     }
               | Ok None ->
                  logError ctx "Error seeding balance history.  No purchases."
                  return unhandled ()
               | Error err ->
                  logError ctx $"Error seeding balance history {err}"
                  return unhandled ()
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

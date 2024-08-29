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

let orgId = Constants.ORG_ID_REMOVE_SOON
let orgName = "Sapphire Optics"

type SocialTransferCandidate = {
   OrgId: OrgId
   PrimaryAccountId: AccountId
   AccountOwnerId: EmployeeId
   AccountOwnerName: {| First: string; Last: string |}
   AccountOwnerEmail: string
   BusinessName: string
}

let socialTransferOrgs =
   [
      {
         OrgId = "7ef9d8f8-741f-4138-8aa8-37ab6e2e576d" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "0b6c7486-4228-411d-8f39-6db9a98faf27" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "d7cbb60f-c7e7-4cf9-b28c-b5da6c57f493" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {| First = "Bích"; Last = "Phương" |}
         AccountOwnerEmail = "bichphuong@gmail.com"
         BusinessName = "Linear"
      }
      {
         OrgId = "6b20162e-61f3-4434-82e0-e7d27337316b" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "2eb09ebc-bb20-4ef8-88c0-15d5e23125f5" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "54804db3-1a1c-42dd-b8bb-94cc71519557" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {|
            First = "Elsieanne"
            Last = "Caplette"
         |}
         AccountOwnerEmail = "elsiane@gmail.com"
         BusinessName = "Figma"
      }
      {
         OrgId = "55e75321-b9ad-48cc-b5ad-74af0a7e31b2" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "7918f574-9600-481f-bbc8-0a0430f5a416" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "54804db3-1a1c-42dd-b8bb-94cc71519557" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {| First = "Paul"; Last = "Haslinger" |}
         AccountOwnerEmail = "haslinger@gmail.com"
         BusinessName = "Lendtable"
      }
      {
         OrgId = "4c6d31a0-51de-4ab8-b805-f61eadb78f81" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "c923710d-610d-4bea-a324-de8a66d072cc" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "c931c059-8bff-4a30-ad5f-fc85b3092c11" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {|
            First = "Finnegan"
            Last = "Swiftshadow"
         |}
         AccountOwnerEmail = "finneganswift@gmail.com"
         BusinessName = "Shopify"
      }
      {
         OrgId = "4ccbb100-c023-4957-82fd-36c35004a9fd" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "a8ffdd09-4502-4054-bb20-1c12e3f26821" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "3565a715-a7e9-4ef2-b81a-628605aabaa0" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {| First = "Mahatma"; Last = "Gandhi" |}
         AccountOwnerEmail = "gandhi@yahoo.com"
         BusinessName = "Dropbox"
      }
      {
         OrgId = "9f7a2ae5-9cb5-46ac-908e-4427230bf0fe" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "60b76142-bded-4eb3-8c87-89ab9949a65e" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "d260f814-186f-451c-8a9c-8ce2e565fbb4" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {| First = "Zhi"; Last = "Ng" |}
         AccountOwnerEmail = "zhi@yahoo.com"
         BusinessName = "Xero"
      }
      {
         OrgId = "5f808408-04a1-45f3-9e45-eba1ecd7a2da" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "24dc1103-0682-4141-a97e-b3870b8fadbf" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "3c2d88fd-d3aa-4999-8e73-d8129404f00b" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {| First = "Meorise"; Last = "Sarlee" |}
         AccountOwnerEmail = "msarlee@yahoo.com"
         BusinessName = "Huntress"
      }
      {
         OrgId = "e6641c29-b980-4a7a-9b5a-24e01ebd9af0" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "ade87fa5-df43-43da-9903-90d66e986bf8" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "ff4b01e1-5d63-4eb1-a03a-11a976cb2f68" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {|
            First = "Nakiasha"
            Last = "Daleth"
         |}
         AccountOwnerEmail = "nakiasha@yahoo.com"
         BusinessName = "Dusty Robotics"
      }
      {
         OrgId = "b854d513-c40e-4582-bf2f-688d6a73a21a" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "29963c00-c366-431e-84c2-cee5a3258581" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "e8e8de39-dce0-4c70-89e4-5ab345949f99" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {| First = "Pruncha"; Last = "Yukio" |}
         AccountOwnerEmail = "yukio@yahoo.com"
         BusinessName = "Github"
      }
      {
         OrgId = "1e01868e-adcb-4c5d-8a8b-f75bc36db0f5" |> Guid.Parse |> OrgId
         PrimaryAccountId =
            "b260b00f-8f09-47c2-9fe5-c6d19742b125" |> Guid.Parse |> AccountId
         AccountOwnerId =
            "20038dd5-e7f2-478d-a57b-6165969471a4" |> Guid.Parse |> EmployeeId
         AccountOwnerName = {| First = "Aio"; Last = "Usagi" |}
         AccountOwnerEmail = "usagi@yahoo.com"
         BusinessName = "Segment"
      }
   ]
   |> List.map (fun o -> o.OrgId, o)
   |> Map.ofList

let socialTransferSenders, socialTransferCandidates =
   socialTransferOrgs
   |> Map.partition (fun _ o -> o.AccountOwnerEmail.Contains "yahoo")

// TODO: Temporarily create org here until fleshed Out
let createOrgs () =
   let myOrg = orgId, orgName

   let otherOrgs =
      socialTransferOrgs.Values
      |> Seq.map (fun o -> o.OrgId, o.BusinessName)
      |> List.ofSeq

   let orgs = myOrg :: otherOrgs

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
      socialTransferOrgs |> Map.toArray |> Array.map (fst >> OrgId.get)

   let accountIds =
      socialTransferOrgs
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
// The same goes for InternalTransferBetweenOrgsDeposited where we have control
// over the timestamp of the initial request but need to modify timestamps
// of the subsequent Deposited events here.
// Modifying system-produced event timestamps via update statements on
// the read models as demonstrated below should be a sufficient strategy
// for observing time based analytics on seed data.
let seedBalanceHistory () = taskResultOption {
   let query =
      $"""
      SELECT {EmployeeEventFields.timestamp}, {EmployeeEventFields.correlationId}
      FROM {EmployeeEventSqlMapper.table}
      WHERE {EmployeeEventFields.name} = 'DebitRequested'

      UNION ALL

      SELECT {TransactionFields.timestamp}, {TransactionFields.correlationId}
      FROM {TransactionSqlMapper.table}
      WHERE {TransactionFields.name} = 'InternalTransferBetweenOrgsPending'
      """

   let! initialRequestsToModify =
      pgQuery query None (fun read -> {|
         CorrelationId = EmployeeEventSqlReader.correlationId read
         Timestamp = EmployeeEventSqlReader.timestamp read
      |})

   let sqlParams =
      initialRequestsToModify
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
      WHERE {TransactionFields.correlationId} = @correlationId;
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
         EmployeeId = Constants.LOGGED_IN_EMPLOYEE_ID_REMOVE_SOON
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

   let socialTransferOrgs =
      socialTransferOrgs
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
                     InitiatedBy = InitiatedById o.AccountOwnerId
                  }
                  |> withModifiedTimestamp

               Map.add o.PrimaryAccountId cmd acc)
            Map.empty
      |> Map.toSeq

   Map [
      cmd1.Data.AccountId, cmd1

      cmd2.Data.AccountId, cmd2

      cmd3.Data.AccountId, cmd3

      yield! socialTransferOrgs
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
         DailyPurchaseLimit = Some 9_310m
         MonthlyPurchaseLimit = None
         InitiatedBy = mockAccountOwnerId
      } with
         Timestamp = cmd.Timestamp.AddHours 1
   }

   let cardCmd2 = {
      cardCmd1 with
         Data.CardId = Guid.NewGuid() |> CardId
         Data.CardNickname = Some "Office Supplies"
         Data.DailyPurchaseLimit = None
         Timestamp = cmd.Timestamp.AddHours 1.1
   }

   cardCmd1, cardCmd2

let mockEmployees =
   let cmds = [
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "pongkool@gmail.com"
         FirstName = "Pop"
         LastName = "Pongkool"
         OrgId = orgId
         Role = Role.Admin
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      }
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "fishinthesea@gmail.com"
         FirstName = "Devon"
         LastName = "Eisenbarger"
         OrgId = orgId
         Role = Role.CardOnly
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      }
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "inkwaruntorn@gmail.com"
         FirstName = "Ink"
         LastName = "Waruntorn"
         OrgId = orgId
         Role = Role.CardOnly
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      }
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "hanzzimmer@gmail.com"
         FirstName = "Hanz"
         LastName = "Zimmer"
         OrgId = orgId
         Role = Role.CardOnly
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      }
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "denvau@gmail.com"
         FirstName = "Den"
         LastName = "Vau"
         OrgId = orgId
         Role = Role.CardOnly
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      }
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "sadeadu@gmail.com"
         FirstName = "Sade"
         LastName = "Adu"
         OrgId = orgId
         Role = Role.CardOnly
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      }
   ]

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
      for cmd in cmds ->
         let cmd = {
            cmd with
               Timestamp = mockAccountOwnerCmd.Timestamp
         }

         EmployeeId.fromEntityId cmd.EntityId, (cmd, createCard cmd)
   ]

let mockEmployeesPendingInviteConfirmation =
   [
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "zikomo@gmail.com"
         FirstName = "Zikomo"
         LastName = "Fwasa"
         OrgId = orgId
         Role = Role.Admin
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      }
      CreateEmployeeCommand.create mockAccountOwnerId {
         Email = "megmeyers@gmail.com"
         FirstName = "Meg"
         LastName = "Meyers"
         OrgId = orgId
         Role = Role.Scholar
         OrgRequiresEmployeeInviteApproval = false
         CardInfo = None
      }
   ]
   |> List.map (fun cmd -> {
      cmd with
         Timestamp = mockAccountOwnerCmd.Timestamp
   })

let randomAmount min max =
   let rnd = new Random()
   decimal (rnd.Next(min, max)) + decimal (rnd.NextDouble())

let seedAccountOwnerActions
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (timestamp: DateTime)
   (account: Account)
   =
   let domesticRecipientCmd =
      RegisterDomesticTransferRecipientCommand.create
         (arCheckingAccountId, orgId)
         mockAccountOwnerId
         {
            AccountId = Guid.NewGuid() |> AccountId
            FirstName = "Microsoft"
            LastName = "Azure"
            AccountNumber = AccountNumber.generate ()
            RoutingNumber = "123456789"
            Depository = DomesticRecipientAccountDepository.Checking
            PaymentNetwork = PaymentNetwork.ACH
         }

   let domesticRecipient =
      domesticRecipientCmd
      |> RegisterDomesticTransferRecipientCommand.toEvent
      |> Result.map (fun evt -> evt.Data.Recipient)
      |> Result.toValueOption
      |> _.Value

   let accountRef = getAccountRef account.AccountId

   let msg =
      domesticRecipientCmd
      |> AccountCommand.RegisterDomesticTransferRecipient
      |> AccountMessage.StateChange

   accountRef <! msg

   for month in [ 1..3 ] do
      let timestamp = timestamp.AddMonths month

      let transferCmd = {
         DomesticTransferCommand.create
            (account.AccountId, orgId)
            mockAccountOwnerId
            {
               ScheduledDate = timestamp
               Amount = 30_000m + (randomAmount 1000 7000)
               Recipient = domesticRecipient
               Sender = {
                  Name = account.Name
                  AccountNumber = account.AccountNumber
                  RoutingNumber = account.RoutingNumber
                  AccountId = account.AccountId
                  OrgId = orgId
               }
               Memo = Some "Azure Bill"
            } with
            Timestamp = timestamp
      }

      let msg =
         transferCmd
         |> AccountCommand.DomesticTransfer
         |> AccountMessage.StateChange

      accountRef <! msg

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

            let ind = int (randomAmount 0 (socialTransferSenders.Length() - 1))
            let sender = socialTransferSenders.Values |> Seq.item ind

            let msg =
               let ts = timestamp.AddDays(float (maxDays - 1))

               {
                  InternalTransferBetweenOrgsCommand.create
                     (sender.PrimaryAccountId, sender.OrgId)
                     (InitiatedById sender.AccountOwnerId)
                     {
                        Memo = None
                        BaseInfo = {
                           Amount = 10_000m + randomAmount 1000 10_000
                           RecipientOrgId = orgId
                           RecipientId = arCheckingAccountId
                           RecipientName = orgName
                           ScheduledDate = ts
                           Sender = {
                              Name = sender.BusinessName
                              AccountId = sender.PrimaryAccountId
                              OrgId = sender.OrgId
                           }
                        }
                     } with
                     Timestamp = ts
               }
               |> AccountCommand.InternalTransferBetweenOrgs
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
                           RecipientName = recipient.BusinessName
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

            let recipient = mockAccounts[opsCheckingAccountId]

            let msg =
               {
                  InternalTransferWithinOrgCommand.create
                     (arCheckingAccountId, orgId)
                     mockAccountOwnerId
                     {
                        BaseInfo = {
                           RecipientOrgId = recipient.OrgId
                           RecipientId = recipient.Data.AccountId
                           RecipientName = recipient.Data.Name
                           Amount = 2000m + randomAmount 1000 2000
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
               |> AccountCommand.InternalTransfer
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

let createAccountOwners getEmployeeRef =
   let createAccountOwnerCmd (business: SocialTransferCandidate) =
      let date = DateTime.Today
      let startOfMonth = DateTime(date.Year, date.Month, 1).ToUniversalTime()
      let ts = startOfMonth.AddMonths -3

      {
         CreateAccountOwnerCommand.create {
            Email = business.AccountOwnerEmail
            FirstName = business.AccountOwnerName.First
            LastName = business.AccountOwnerName.Last
            OrgId = business.OrgId
            EmployeeId = business.AccountOwnerId
         } with
            Timestamp = ts
      }

   let socialTransferAccountOwners =
      socialTransferOrgs.Values |> Seq.toList |> List.map createAccountOwnerCmd

   for cmd in mockAccountOwnerCmd :: socialTransferAccountOwners do
      let employeeId = cmd.Data.EmployeeId

      let createMsg =
         cmd
         |> EmployeeCommand.CreateAccountOwner
         |> EmployeeMessage.StateChange

      let confirmInviteCmd =
         ConfirmInvitationCommand.create (employeeId, cmd.OrgId) {
            Email = Email.deserialize cmd.Data.Email
            Reference = None
            AuthProviderUserId = Guid.NewGuid()
         }
         |> EmployeeCommand.ConfirmInvitation
         |> EmployeeMessage.StateChange

      let employeeRef = getEmployeeRef employeeId
      employeeRef <! createMsg
      employeeRef <! confirmInviteCmd

let createEmployees
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   =
   for employeeCreateCmd in mockEmployeesPendingInviteConfirmation do
      let employeeRef =
         getEmployeeRef (EmployeeId.fromEntityId employeeCreateCmd.EntityId)

      let msg =
         employeeCreateCmd
         |> EmployeeCommand.CreateEmployee
         |> EmployeeMessage.StateChange

      employeeRef <! msg

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
      let! (employeeOpt: Employee option) =
         employeeRef <? EmployeeMessage.GetEmployee

      return option {
         let! employee = employeeOpt
         let! card = employee.Cards.TryFind cardId
         return employee, card
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

         let! account = accountRef <? AccountMessage.GetAccount

         match account with
         | None ->
            logError
               mailbox
               $"Can not proceed with account owner actions - eId: {accountOwnerId}"
         | Some account ->
            seedAccountOwnerActions
               getAccountRef
               businessCardCreateCmd.Timestamp
               account

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
                     createAccountOwners getEmployeeRef

                     do! Task.Delay 10_000

                     let remaining =
                        getRemainingAccountsToCreate
                           verified
                           state.AccountsToCreate

                     for command in remaining.Values do
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

               do! Task.Delay 40_000
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

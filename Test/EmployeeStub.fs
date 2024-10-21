[<RequireQualifiedAccess>]
module EmployeeStub

open System

open Lib.SharedTypes
open Bank.Employee.Domain

let accountId = Guid.NewGuid() |> AccountId
let accountNumber = AccountNumber.generate () |> int64 |> AccountNumber
let employeeId = Guid.NewGuid() |> EmployeeId
let orgId = Guid.NewGuid() |> OrgId
let compositeId = employeeId, orgId
let correlationId = Guid.NewGuid() |> CorrelationId
let cardId = Guid.NewGuid() |> CardId
let cardNumberLast4 = "9310"
let initiatedById = Guid.NewGuid() |> EmployeeId |> InitiatedById

let purchaseInfo: DebitInfo = {
   CardId = cardId
   AccountId = accountId
   EmployeeId = employeeId
   CorrelationId = correlationId
   CardNumberLast4 = cardNumberLast4
   Date = DateTime.UtcNow
   Amount = 10m
   Origin = "Groceries"
   Reference = None
}

let command =
   let dailyPurchaseLimit = 2000m
   let monthlyPurchaseLimit = 20_000m

   {|
      createEmployee = {
         CreateEmployeeCommand.create initiatedById {
            OrgId = orgId
            OrgRequiresEmployeeInviteApproval = false
            Role = Role.Admin
            FirstName = "Dan"
            LastName = "Eis"
            Email = "jellyfish@gmail.com"
            CardInfo = None
         } with
            EntityId = EmployeeId.toEntityId employeeId
      }
      createCard =
         CreateCardCommand.create {
            Virtual = true
            PersonName = "Dan Eis"
            CardNickname = Some "Travel"
            CardType = CardType.Debit
            InitiatedBy = initiatedById
            DailyPurchaseLimit = Some dailyPurchaseLimit
            MonthlyPurchaseLimit = Some monthlyPurchaseLimit
            AccountId = accountId
            OrgId = orgId
            EmployeeId = employeeId
            CardId = cardId
         }
      debit =
         fun amount -> {
            DebitRequestCommand.create compositeId {
               CardId = purchaseInfo.CardId
               CardNumberLast4 = purchaseInfo.CardNumberLast4
               AccountId = purchaseInfo.AccountId
               Date = purchaseInfo.Date
               Amount = amount
               Origin = purchaseInfo.Origin
               Reference = purchaseInfo.Reference
            } with
               CorrelationId = correlationId
         }
      approveDebit =
         ApproveDebitCommand.create compositeId { Info = purchaseInfo }
      limitDailyDebits =
         fun amount ->
            LimitDailyDebitsCommand.create compositeId initiatedById {
               PriorLimit = dailyPurchaseLimit
               DebitLimit = amount
               CardId = cardId
               CardNumberLast4 = cardNumberLast4
            }
      limitMonthlyDebits =
         fun amount ->
            LimitMonthlyDebitsCommand.create compositeId initiatedById {
               PriorLimit = monthlyPurchaseLimit
               DebitLimit = amount
               CardId = cardId
               CardNumberLast4 = cardNumberLast4
            }
      lockCard =
         LockCardCommand.create compositeId initiatedById {
            CardId = cardId
            Reference = None
            CardNumberLast4 = cardNumberLast4
         }
      unlockCard =
         UnlockCardCommand.create compositeId initiatedById {
            CardId = cardId
            Reference = None
            CardNumberLast4 = cardNumberLast4
         }
   |}

type EventIndex = {
   employeeCreated: BankEvent<CreatedEmployee>
   cardCreated: BankEvent<CreatedCard>
}

let event: EventIndex = {
   employeeCreated =
      command.createEmployee
      |> CreateEmployeeCommand.toEvent
      |> Result.toValueOption
      |> _.Value
   cardCreated =
      command.createCard
      |> CreateCardCommand.toEvent
      |> Result.toValueOption
      |> _.Value
}

let employeeState =
   let e = command.createEmployee

   {
      Employee.empty with
         EmployeeId = EmployeeId.fromEntityId e.EntityId
         OrgId = e.OrgId
         FirstName = e.Data.FirstName
         LastName = e.Data.LastName
         Email = Email.deserialize e.Data.Email
         Role = e.Data.Role
         Status = EmployeeStatus.Active
         Cards =
            Map [
               event.cardCreated.Data.Card.CardId, event.cardCreated.Data.Card
            ]
   }

let employeeStateWithEvents: EmployeeWithEvents = {
   Info = employeeState
   Events = [
      EmployeeEnvelope.wrap event.employeeCreated
      EmployeeEnvelope.wrap event.cardCreated
   ]
}

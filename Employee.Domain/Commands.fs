namespace Bank.Employee.Domain

open Validus
open System

open Lib.SharedTypes
open Lib.Validators

type CreateEmployeeInput = {
   EmployeeId: EmployeeId
   Email: string
   FirstName: string
   LastName: string
   OrgId: OrgId
   Role: EmployeeRole
}

type CreateEmployeeCommand = Command<CreateEmployeeInput>

module CreateEmployeeCommand =
   let create (data: CreateEmployeeInput) =
      Command.create
         (EmployeeId.toEntityId data.EmployeeId)
         data.OrgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: CreateEmployeeCommand)
      : ValidationResult<BankEvent<CreatedEmployee>>
      =
      validate {
         let input = cmd.Data
         let! firstName = firstNameValidator input.FirstName
         and! lastName = lastNameValidator input.LastName
         and! email = Email.ofString "Create employee email" input.Email

         return
            BankEvent.create2<CreateEmployeeInput, CreatedEmployee> cmd {
               Email = email
               FirstName = firstName
               LastName = lastName
               Role = input.Role
            }
      }

type CreateCardInput = {
   PersonName: string
   CardNickname: string option
   OrgId: OrgId
   EmployeeId: EmployeeId
   AccountId: AccountId
   CardId: CardId
   Virtual: bool
}

type CreateCardCommand = Command<CreateCardInput>

module CreateCardCommand =
   let create (data: CreateCardInput) =
      Command.create
         (EmployeeId.toEntityId data.EmployeeId)
         data.OrgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: CreateCardCommand)
      : ValidationResult<BankEvent<CreatedCard>>
      =
      validate {
         let input = cmd.Data
         let random = System.Random()

         let randomNum (numDigits: int) =
            List.init numDigits (fun _ -> random.Next(1, 9) |> string)
            |> String.concat ""

         return
            BankEvent.create2<CreateCardInput, CreatedCard> cmd {
               Info = {
                  SecurityInfo = {
                     PersonName = input.PersonName
                     CardNumber = randomNum 16 |> Int64.Parse |> CardNumber
                     Expiration = DateTime.Now.AddYears(3)
                     CVV = randomNum 3 |> Int16.Parse |> CVV
                  }
                  CardNickname = input.CardNickname
                  CardId = cmd.Data.CardId
                  AccountId = cmd.Data.AccountId
                  DailyDebitLimit = 2000m
                  DailyDebitAccrued = 0m
                  LastDebitDate = None
                  Locked = false
                  Virtual = input.Virtual
               }
            }
      }

type DebitRequestInput = {
   CardId: CardId
   AccountId: AccountId
   Amount: decimal
   Origin: string
   Reference: string option
   Date: DateTime
}

type DebitRequestCommand = Command<DebitRequestInput>

// NOTE: CorrelationId traced from
// EmployeeCommand.DebitRequest
// -> AccountCommand.Debit
// -> EmployeeCommand.ApproveDebit/DeclineDebit
module DebitRequestCommand =
   let create (employeeId: EmployeeId, orgId: OrgId) (data: DebitRequestInput) =
      Command.create
         (EmployeeId.toEntityId employeeId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: DebitRequestCommand)
      : ValidationResult<BankEvent<DebitRequested>>
      =
      validate {
         let input = cmd.Data
         let! amount = amountValidator "Debit amount" input.Amount
         let! date = dateNotDefaultValidator "Date" input.Date
         let! origin = originValidator input.Origin

         return
            BankEvent.create2<DebitRequestInput, DebitRequested> cmd {
               Info = {
                  EmployeeId = EmployeeId.fromEntityId cmd.EntityId
                  CorrelationId = cmd.CorrelationId
                  CardId = input.CardId
                  AccountId = input.AccountId
                  Amount = amount
                  Origin = origin
                  Reference = cmd.Data.Reference
                  Date = date
               }
            }
      }

type ApproveDebitCommand = Command<DebitApproved>

module ApproveDebitCommand =
   let create (employeeId: EmployeeId, orgId: OrgId) (data: DebitApproved) =
      Command.create
         (EmployeeId.toEntityId employeeId)
         orgId
         data.Info.CorrelationId
         data

   let toEvent
      (cmd: ApproveDebitCommand)
      : ValidationResult<BankEvent<DebitApproved>>
      =
      BankEvent.create<DebitApproved> cmd |> Ok

type DeclineDebitCommand = Command<DebitDeclined>

module DeclineDebitCommand =
   let create (employeeId: EmployeeId, orgId: OrgId) (data: DebitDeclined) =
      Command.create
         (EmployeeId.toEntityId employeeId)
         orgId
         data.Info.CorrelationId
         data

   let toEvent
      (cmd: DeclineDebitCommand)
      : ValidationResult<BankEvent<DebitDeclined>>
      =
      BankEvent.create<DebitDeclined> cmd |> Ok

type LimitDailyDebitsCommand = Command<DailyDebitLimitUpdated>

module LimitDailyDebitsCommand =
   let create
      (employeeId: EmployeeId, orgId: OrgId)
      (data: DailyDebitLimitUpdated)
      =
      Command.create
         (EmployeeId.toEntityId employeeId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: LimitDailyDebitsCommand)
      : ValidationResult<BankEvent<DailyDebitLimitUpdated>>
      =
      validate {
         let! _ = amountValidator "Debit limit" cmd.Data.DebitLimit

         return BankEvent.create<DailyDebitLimitUpdated> cmd
      }

type LockCardCommand = Command<LockedCard>

module LockCardCommand =
   let create (employeeId: EmployeeId, orgId: OrgId) (data: LockedCard) =
      Command.create
         (EmployeeId.toEntityId employeeId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: LockCardCommand)
      : ValidationResult<BankEvent<LockedCard>>
      =
      Ok <| BankEvent.create<LockedCard> cmd

type UnlockCardCommand = Command<UnlockedCard>

module UnlockCardCommand =
   let create (employeeId: EmployeeId, orgId: OrgId) (data: UnlockedCard) =
      Command.create
         (EmployeeId.toEntityId employeeId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: UnlockCardCommand)
      : ValidationResult<BankEvent<UnlockedCard>>
      =
      Ok <| BankEvent.create<UnlockedCard> cmd

namespace Bank.Account.Domain

open System
open Validus

open Lib.SharedTypes
open Lib.Validators
open MaintenanceFee

type CreateAccountInput = {
   Email: string
   Balance: decimal
   FirstName: string
   LastName: string
   Currency: Currency
}

type CreateAccountCommand = Command<CreateAccountInput>

module CreateAccountCommand =
   let create accountId (data: CreateAccountInput) =
      Command.create accountId (guid ()) data

   let toEvent
      (cmd: CreateAccountCommand)
      : ValidationResult<BankEvent<CreatedAccount>>
      =
      validate {
         let input = cmd.Data
         let! _ = firstNameValidator input.FirstName
         and! _ = lastNameValidator input.LastName

         and! _ =
            Check.Decimal.greaterThanOrEqualTo 100m "Balance" input.Balance

         and! email = Email.ofString "Create account email" input.Email

         return
            BankEvent.create<CreateAccountInput, CreatedAccount> cmd {
               Email = email
               FirstName = input.FirstName
               LastName = input.LastName
               Balance = input.Balance
               Currency = input.Currency
            }
      }

type DepositCashInput = {
   Date: DateTime
   Amount: decimal
   Origin: string option
}

type DepositCashCommand = Command<DepositCashInput>

module DepositCashCommand =
   let create entityId (data: DepositCashInput) =
      Command.create entityId (guid ()) data

   let toEvent
      (cmd: DepositCashCommand)
      : ValidationResult<BankEvent<DepositedCash>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Deposit amount" input.Amount
         let! _ = dateNotDefaultValidator "Date" input.Date

         return
            BankEvent.create<DepositCashInput, DepositedCash> cmd {
               DepositedAmount = input.Amount
               Origin = input.Origin |> Option.defaultValue "ATM"
            }
      }

type DebitInput = {
   Date: DateTime
   Amount: decimal
   Origin: string
   Reference: string option
}

type DebitCommand = Command<DebitInput>

module DebitCommand =
   let create entityId (data: DebitInput) =
      Command.create entityId (guid ()) data

   let toEvent
      (cmd: DebitCommand)
      : ValidationResult<BankEvent<DebitedAccount>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Debit amount" input.Amount
         let! _ = dateNotDefaultValidator "Date" input.Date
         let! _ = originValidator input.Origin

         return
            BankEvent.create<DebitInput, DebitedAccount> cmd {
               DebitedAmount = input.Amount
               Origin = input.Origin
               Date = input.Date
               Reference = input.Reference
            }
      }

type LimitDailyDebitsInput = { DebitLimit: decimal }
type LimitDailyDebitsCommand = Command<LimitDailyDebitsInput>

module LimitDailyDebitsCommand =
   let create entityId (data: LimitDailyDebitsInput) =
      Command.create entityId (guid ()) data

   let toEvent
      (cmd: LimitDailyDebitsCommand)
      : ValidationResult<BankEvent<DailyDebitLimitUpdated>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Debit limit" input.DebitLimit

         return
            BankEvent.create<LimitDailyDebitsInput, DailyDebitLimitUpdated> cmd {
               DebitLimit = input.DebitLimit
            }
      }

type LockCardInput = { Reference: string option }
type LockCardCommand = Command<LockCardInput>

module LockCardCommand =
   let create entityId (data: LockCardInput) =
      Command.create entityId (guid ()) data

   let toEvent
      (cmd: LockCardCommand)
      : ValidationResult<BankEvent<LockedCard>>
      =
      Ok
      <| BankEvent.create<LockCardInput, LockedCard> cmd {
         Reference = cmd.Data.Reference
      }

type UnlockCardInput = { Reference: string option }
type UnlockCardCommand = Command<UnlockCardInput>

module UnlockCardCommand =
   let create entityId (data: UnlockCardInput) =
      Command.create entityId (guid ()) data

   let toEvent
      (cmd: UnlockCardCommand)
      : ValidationResult<BankEvent<UnlockedCard>>
      =
      Ok
      <| BankEvent.create<UnlockCardInput, UnlockedCard> cmd {
         Reference = cmd.Data.Reference
      }

type MaintenanceFeeInput = { Amount: decimal }
type MaintenanceFeeCommand = Command<MaintenanceFeeInput>

module MaintenanceFeeCommand =
   let create entityId =
      Command.create entityId (guid ()) {
         Amount = MaintenanceFee.RecurringDebitAmount
      }

   let toEvent
      (cmd: MaintenanceFeeCommand)
      : ValidationResult<BankEvent<MaintenanceFeeDebited>>
      =
      Ok
      <| BankEvent.create<MaintenanceFeeInput, MaintenanceFeeDebited> cmd {
         DebitedAmount = cmd.Data.Amount
      }


type SkipMaintenanceFeeInput = { Reason: MaintenanceFeeCriteria }
type SkipMaintenanceFeeCommand = Command<SkipMaintenanceFeeInput>

module SkipMaintenanceFeeCommand =
   let create entityId (data: SkipMaintenanceFeeInput) =
      Command.create entityId (guid ()) data

   let toEvent
      (cmd: SkipMaintenanceFeeCommand)
      : ValidationResult<BankEvent<MaintenanceFeeSkipped>>
      =
      Ok
      <| BankEvent.create<SkipMaintenanceFeeInput, MaintenanceFeeSkipped> cmd {
         Reason = cmd.Data.Reason
      }

type CloseAccountInput = { Reference: string option }
type CloseAccountCommand = Command<CloseAccountInput>

module CloseAccountCommand =
   let create entityId (data: CloseAccountInput) =
      Command.create entityId (guid ()) data

   let toEvent
      (cmd: CloseAccountCommand)
      : ValidationResult<BankEvent<AccountClosed>>
      =
      Ok
      <| BankEvent.create<CloseAccountInput, AccountClosed> cmd {
         Reference = cmd.Data.Reference
      }

type StartBillingCycleInput = { Reference: string option }
type StartBillingCycleCommand = Command<StartBillingCycleInput>

module StartBillingCycleCommand =
   let create entityId (data: StartBillingCycleInput) =
      Command.create entityId (guid ()) data

   let toEvent
      (cmd: StartBillingCycleCommand)
      : ValidationResult<BankEvent<BillingCycleStarted>>
      =
      Ok
      <| BankEvent.create<StartBillingCycleInput, BillingCycleStarted> cmd {
         Reference = cmd.Data.Reference
      }

namespace Bank.Account.Domain

open Validus

open Lib.SharedTypes
open Lib.Validators

type CreateAccountInput = {
   Email: string
   Balance: decimal
   FirstName: string
   LastName: string
   Currency: Currency
   AccountId: AccountId
   OrgId: OrgId
}

type CreateAccountCommand = Command<CreateAccountInput>

module CreateAccountCommand =
   let create (data: CreateAccountInput) =
      Command.create
         (AccountId.toEntityId data.AccountId)
         data.OrgId
         (CorrelationId.create ())
         data

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
            BankEvent.create2<CreateAccountInput, CreatedAccount> cmd {
               Email = email
               FirstName = input.FirstName
               LastName = input.LastName
               Balance = input.Balance
               Currency = input.Currency
            }
      }

type DepositCashInput = {
   Amount: decimal
   Origin: string option
}

type DepositCashCommand = Command<DepositCashInput>

module DepositCashCommand =
   let create (accountId: AccountId, orgId: OrgId) (data: DepositCashInput) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: DepositCashCommand)
      : ValidationResult<BankEvent<DepositedCash>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Deposit amount" input.Amount

         return
            BankEvent.create2<DepositCashInput, DepositedCash> cmd {
               Amount = input.Amount
               Origin = input.Origin |> Option.defaultValue "ATM"
            }
      }

type DebitCommand = Command<DebitedAccount>

module DebitCommand =
   let create (accountId: AccountId, orgId: OrgId) (data: DebitedAccount) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: DebitCommand)
      : ValidationResult<BankEvent<DebitedAccount>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Debit amount" input.Amount
         let! _ = dateNotDefaultValidator "Date" input.Date
         let! _ = originValidator input.Origin

         return BankEvent.create<DebitedAccount> cmd
      }

type LimitDailyDebitsCommand = Command<DailyDebitLimitUpdated>

module LimitDailyDebitsCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: DailyDebitLimitUpdated)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: LimitDailyDebitsCommand)
      : ValidationResult<BankEvent<DailyDebitLimitUpdated>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Debit limit" input.DebitLimit

         return BankEvent.create<DailyDebitLimitUpdated> cmd
      }

type LockCardCommand = Command<LockedCard>

module LockCardCommand =
   let create (accountId: AccountId, orgId: OrgId) (data: LockedCard) =
      Command.create
         (AccountId.toEntityId accountId)
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
   let create (accountId: AccountId, orgId: OrgId) (data: UnlockedCard) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: UnlockCardCommand)
      : ValidationResult<BankEvent<UnlockedCard>>
      =
      Ok <| BankEvent.create<UnlockedCard> cmd

type MaintenanceFeeCommand = Command<MaintenanceFeeDebited>

module MaintenanceFeeCommand =
   let create (accountId: AccountId, orgId: OrgId) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         {
            Amount = MaintenanceFee.RecurringDebitAmount
         }

   let toEvent
      (cmd: MaintenanceFeeCommand)
      : ValidationResult<BankEvent<MaintenanceFeeDebited>>
      =
      Ok <| BankEvent.create<MaintenanceFeeDebited> cmd


type SkipMaintenanceFeeCommand = Command<MaintenanceFeeSkipped>

module SkipMaintenanceFeeCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: MaintenanceFeeSkipped)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: SkipMaintenanceFeeCommand)
      : ValidationResult<BankEvent<MaintenanceFeeSkipped>>
      =
      Ok <| BankEvent.create<MaintenanceFeeSkipped> cmd

type CloseAccountCommand = Command<AccountClosed>

module CloseAccountCommand =
   let create (accountId: AccountId, orgId: OrgId) (data: AccountClosed) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: CloseAccountCommand)
      : ValidationResult<BankEvent<AccountClosed>>
      =
      Ok <| BankEvent.create<AccountClosed> cmd

type StartBillingCycleCommand = Command<BillingCycleStarted>

module StartBillingCycleCommand =
   let create (accountId: AccountId, orgId: OrgId) (data: BillingCycleStarted) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: StartBillingCycleCommand)
      : ValidationResult<BankEvent<BillingCycleStarted>>
      =
      Ok <| BankEvent.create<BillingCycleStarted> cmd

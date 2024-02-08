namespace Bank.Account.Domain

open System
open Validus

open Lib.Types
open Lib.Validators
open MaintenanceFee

type CreateAccountCommand
   (
      entityId,
      email: string,
      balance: decimal,
      firstName: string,
      lastName: string,
      currency: Currency,
      correlationId
   ) =
   inherit Command(entityId, correlationId)
   member x.Email = email
   member x.Currency = currency
   member x.Balance = balance
   member x.FirstName = firstName
   member x.LastName = lastName

   member x.toEvent() : ValidationResult<BankEvent<CreatedAccount>> = validate {
      let! _ = nameValidator "First name" x.FirstName
      and! _ = nameValidator "Last name" x.LastName
      and! _ = Check.Decimal.greaterThanOrEqualTo 100m "Balance" x.Balance
      and! email = Email.ofString "Create account email" x.Email

      return {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            Email = email
            FirstName = x.FirstName
            LastName = x.LastName
            Balance = x.Balance
            Currency = x.Currency
         }
         CorrelationId = x.CorrelationId
      }
   }

type DepositCashCommand
   (entityId, date: DateTime, amount: decimal, origin: string, correlationId) =
   inherit Command(entityId, correlationId)
   member x.Date = date
   member x.Amount = amount
   member x.Origin = if isNull origin then "ATM" else origin

   member x.toEvent() : ValidationResult<BankEvent<DepositedCash>> = validate {
      let! _ = amountValidator "Deposit amount" x.Amount
      let! _ = transactionDateValidator "Date" x.Date

      return {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            DepositedAmount = x.Amount
            Origin = x.Origin
         }
         CorrelationId = x.CorrelationId
      }
   }

type DebitCommand
   (
      entityId,
      date: DateTime,
      amount: decimal,
      origin: string,
      reference: string,
      correlationId
   ) =
   inherit Command(entityId, correlationId)
   member x.Date = date
   member x.Amount = amount
   member x.Origin = origin
   member x.Reference = reference

   member x.toEvent() : ValidationResult<BankEvent<DebitedAccount>> = validate {
      let! _ = amountValidator "Debit amount" x.Amount
      let! _ = transactionDateValidator "Date" x.Date

      return {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            DebitedAmount = x.Amount
            Origin = x.Origin
            Date = x.Date
            Reference =
               if String.IsNullOrEmpty x.Reference then
                  None
               else
                  Some x.Reference
         }
         CorrelationId = x.CorrelationId
      }
   }

type LimitDailyDebitsCommand(entityId, debitLimit: decimal, correlationId) =
   inherit Command(entityId, correlationId)
   member x.DebitLimit = debitLimit

   member x.toEvent() : ValidationResult<BankEvent<DailyDebitLimitUpdated>> = validate {
      let! _ = amountValidator "Debit limit" x.DebitLimit

      return {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = { DebitLimit = x.DebitLimit }
         CorrelationId = x.CorrelationId
      }
   }

type LockCardCommand(entityId, reference: string, correlationId) =
   inherit Command(entityId, correlationId)
   member x.Reference = reference

   member x.toEvent() : ValidationResult<BankEvent<LockedCard>> =
      Ok {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            Reference =
               if String.IsNullOrEmpty x.Reference then
                  None
               else
                  Some x.Reference
         }
         CorrelationId = x.CorrelationId
      }

type UnlockCardCommand(entityId, reference: string, correlationId) =
   inherit Command(entityId, correlationId)
   member x.Reference = reference

   member x.toEvent() : ValidationResult<BankEvent<UnlockedCard>> =
      Ok {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            Reference =
               if String.IsNullOrEmpty x.Reference then
                  None
               else
                  Some x.Reference
         }
         CorrelationId = x.CorrelationId
      }

type MaintenanceFeeCommand(entityId) =
   inherit Command(entityId, correlationId = Guid.Empty)
   member x.Amount = MaintenanceFee.RecurringDebitAmount

   member x.toEvent() : ValidationResult<BankEvent<MaintenanceFeeDebited>> =
      Ok {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = { DebitedAmount = x.Amount }
         CorrelationId = x.CorrelationId
      }

type SkipMaintenanceFeeCommand(entityId, reason: MaintenanceFeeCriteria) =
   inherit Command(entityId, correlationId = Guid.Empty)
   member x.Reason = reason

   member x.toEvent() : ValidationResult<BankEvent<MaintenanceFeeSkipped>> =
      Ok {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = { Reason = x.Reason }
         CorrelationId = x.CorrelationId
      }

type CloseAccountCommand(entityId, reference: string) =
   inherit Command(entityId, correlationId = Guid.Empty)
   member x.Reference = reference

   member x.toEvent() : ValidationResult<BankEvent<AccountClosed>> =
      Ok {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            Reference =
               if String.IsNullOrEmpty x.Reference then
                  None
               else
                  Some x.Reference
         }
         CorrelationId = x.CorrelationId
      }

type StartBillingCycleCommand(entityId, balance) =
   inherit Command(entityId, correlationId = Guid.Empty)
   member x.Balance = balance

   member x.toEvent() : ValidationResult<BankEvent<BillingCycleStarted>> =
      Ok {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = { Balance = x.Balance }
         CorrelationId = x.CorrelationId
      }

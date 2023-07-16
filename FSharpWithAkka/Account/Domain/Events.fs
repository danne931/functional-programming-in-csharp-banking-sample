namespace Bank.Account.Domain

open System

open Lib.Types

type CreatedAccount = {
   FirstName: string
   LastName: string
   Balance: decimal
   Currency: Currency
}

module CreatedAccountEvent =
   let create (cmd: CreateAccountCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         FirstName = cmd.FirstName
         LastName = cmd.LastName
         Balance = cmd.Balance
         Currency = cmd.Currency
      }
      CorrelationId = cmd.CorrelationId
   }

type LockedCard = { Reference: string option }

module LockedCardEvent =
   let create (cmd: LockCardCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         Reference =
            if String.IsNullOrEmpty cmd.Reference then
               None
            else
               Some cmd.Reference
      }
      CorrelationId = cmd.CorrelationId
   }

type UnlockedCard = { Reference: string option }

module UnlockedCardEvent =
   let create (cmd: UnlockCardCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         Reference =
            if String.IsNullOrEmpty cmd.Reference then
               None
            else
               Some cmd.Reference
      }
      CorrelationId = cmd.CorrelationId
   }

type DepositedCash = {
   DepositedAmount: decimal
   Origin: string
}

module DepositedCashEvent =
   let create (cmd: DepositCashCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         DepositedAmount = cmd.Amount
         Origin = cmd.Origin
      }
      CorrelationId = cmd.CorrelationId
   }

type DebitedAccount = {
   Date: DateTime
   DebitedAmount: decimal
   Origin: string
   Reference: string option
}

module DebitedAccountEvent =
   let create (cmd: DebitCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         DebitedAmount = cmd.Amount
         Origin = cmd.Origin
         Date = cmd.Date
         Reference =
            if String.IsNullOrEmpty cmd.Reference then
               None
            else
               Some cmd.Reference
      }
      CorrelationId = cmd.CorrelationId
   }

type DailyDebitLimitUpdated = { DebitLimit: decimal }

module DailyDebitLimitUpdatedEvent =
   let create
      (cmd: LimitDailyDebitsCommand)
      : BankEvent<DailyDebitLimitUpdated>
      =
      {
         EntityId = cmd.EntityId
         Timestamp = cmd.Timestamp
         Data = { DebitLimit = cmd.DebitLimit }
         CorrelationId = cmd.CorrelationId
      }

type MaintenanceFeeDebited = { DebitedAmount: decimal }

module MaintenanceFeeEvent =
   let create (cmd: MaintenanceFeeCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = { DebitedAmount = cmd.Amount }
      CorrelationId = cmd.CorrelationId
   }

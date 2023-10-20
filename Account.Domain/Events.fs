namespace Bank.Account.Domain

open System

open Lib.Types
open MaintenanceFee

type CreatedAccount = {
   Email: Email
   FirstName: string
   LastName: string
   Balance: decimal
   Currency: Currency
}

type LockedCard = { Reference: string option }

type UnlockedCard = { Reference: string option }

type DepositedCash = {
   DepositedAmount: decimal
   Origin: string
}

type DebitedAccount = {
   Date: DateTime
   DebitedAmount: decimal
   Origin: string
   Reference: string option
}

type DailyDebitLimitUpdated = { DebitLimit: decimal }

type MaintenanceFeeDebited = { DebitedAmount: decimal }
type MaintenanceFeeSkipped = { Reason: MaintenanceFeeCriteria }

type AccountClosed = { Reference: string option }

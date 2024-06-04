namespace Bank.Account.Domain

open System

open Lib.SharedTypes
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

type DepositedCash = { Amount: decimal; Origin: string }

type DebitedAccount = {
   Date: DateTime
   Amount: decimal
   Origin: string
   Reference: string option
}

type DailyDebitLimitUpdated = { DebitLimit: decimal }

type MaintenanceFeeDebited = { Amount: decimal }
type MaintenanceFeeSkipped = { Reason: MaintenanceFeeCriteria }

type AccountClosed = { Reference: string option }

type BillingCycleStarted = { Reference: string option }

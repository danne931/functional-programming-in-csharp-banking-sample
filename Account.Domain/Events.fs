namespace Bank.Account.Domain

open System

open Lib.SharedTypes
open MaintenanceFee

type CreatedAccount = {
   Name: string
   Depository: AccountDepository
   Balance: decimal
   Currency: Currency
   AccountNumber: AccountNumber
   RoutingNumber: RoutingNumber
}

type DepositedCash = { Amount: decimal; Origin: string }

type EmployeePurchaseReference = {
   EmployeeName: string
   EmployeeCardNumberLast4: string
   EmployeeId: EmployeeId
   CardId: CardId
}

type DebitedAccount = {
   Date: DateTime
   Amount: decimal
   Origin: string
   Reference: string option
   EmployeePurchaseReference: EmployeePurchaseReference
}

type MaintenanceFeeDebited = { Amount: decimal }
type MaintenanceFeeSkipped = { Reason: MaintenanceFeeCriteria }

type AccountClosed = { Reference: string option }

type BillingCycleStarted = { Reference: string option }

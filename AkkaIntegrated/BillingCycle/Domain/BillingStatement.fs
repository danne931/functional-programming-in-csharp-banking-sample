module BillingStatement

open System
open FsToolkit.ErrorHandling

open Lib.Types
open BankTypes

type BillingStatement = {
   Transactions: AccountEvent list
   Month: int
   Year: int
   Balance: decimal
   Name: string
   AccountId: Guid
}

type BillingStatementDto = {
   Transactions: string
   Month: int
   Year: int
   Balance: decimal
   Name: string
   AccountId: Guid
}

type BillingPersistence = {
   saveBillingStatements: BillingStatement list -> TaskResult<int list, Err>
}

let create
   (account: AccountState)
   (txns: AccountEvent list)
   : BillingStatement
   =
   {
      Transactions = txns
      Month = DateTime.Today.Month
      Year = DateTime.Today.Year
      Balance = account.Balance
      Name = account.Name
      AccountId = account.EntityId
   }

let toDto (statement: BillingStatement) : BillingStatementDto = {
   Transactions = Serialization.serialize statement.Transactions
   Month = statement.Month
   Year = statement.Year
   Balance = statement.Balance
   Name = statement.Name
   AccountId = statement.AccountId
}

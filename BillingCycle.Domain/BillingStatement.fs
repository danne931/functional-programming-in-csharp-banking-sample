module BillingStatement

open System
open FsToolkit.ErrorHandling

open Lib.Types

type BillingTransaction = {
   EventId: Guid
   Name: string
   Amount: decimal
   Date: DateTime
   Info: string
}

type BillingStatement = {
   Transactions: BillingTransaction list
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
   (account:
      {|
         Name: string
         Balance: decimal
         AccountId: Guid
      |})
   (txns: BillingTransaction list)
   : BillingStatement
   =
   {
      Transactions = txns
      Month = DateTime.Today.Month
      Year = DateTime.Today.Year
      Balance = account.Balance
      Name = account.Name
      AccountId = account.AccountId
   }

let toDto (statement: BillingStatement) : BillingStatementDto = {
   Transactions = Serialization.serialize statement.Transactions
   Month = statement.Month
   Year = statement.Year
   Balance = statement.Balance
   Name = statement.Name
   AccountId = statement.AccountId
}

type BillingMessage =
   | RegisterBillingStatement of BillingStatement
   | PersistBillingStatements
   | PersistBillingStatementsResponse of Result<int list, Err>
   | GetWriteReadyStatements
   | BillingCycleFanout
   | BillingCycleFinished

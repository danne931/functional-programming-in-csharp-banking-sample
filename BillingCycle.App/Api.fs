module Bank.BillingCycle.Api

open Lib.Postgres
open BillingStatement

let getBillingStatement () =
   pgQuery<BillingStatement> "SELECT * FROM billingstatements" None
   <| fun (read: RowReader) -> {
      Transactions = read.fieldValue<BillingTransaction list> "transactions"
      Month = read.int "month"
      Year = read.int "year"
      Balance = read.decimal "balance"
      Name = read.text "name"
      AccountId = read.uuid "account_id"
   }

let private billingStatementToSqlParams (statement: BillingStatement) =
   let dto = toDto statement

   [
      "@transactions", Sql.jsonb dto.Transactions
      "@month", Sql.int dto.Month
      "@year", Sql.int dto.Year
      "@balance", Sql.money dto.Balance
      "@name", Sql.text dto.Name
      "@accountId", Sql.uuid dto.AccountId
   ]


let saveBillingStatements (statements: BillingStatement list) =
   let sqlParams = List.map billingStatementToSqlParams statements

   pgTransaction [
      "INSERT into billingstatements \
         (transactions, month, year, balance, name, account_id) \
         VALUES (@transactions, @month, @year, @balance, @name, @accountId)",
      sqlParams
   ]

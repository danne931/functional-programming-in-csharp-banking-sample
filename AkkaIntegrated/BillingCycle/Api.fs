module Bank.BillingCycle.Api

open Lib.Postgres
open BillingStatement
open BankTypes

let getBillingStatement () =
   pgQuery<BillingStatement>
      "SELECT * FROM billingstatements"
      None
      BillingStatement.pgMapper

let saveBillingStatement (account: AccountState) (txns: AccountEvent list) = task {
   let statement = create account txns
   let dto = toDto statement

   let! _ =
      pgPersist "INSERT into billingstatements \
            (transactions, month, year, balance, name, account_id) \
            VALUES (@transactions, @month, @year, @balance, @name, @accountId)" [
         "@transactions", Sql.jsonb dto.Transactions
         "@month", Sql.int dto.Month
         "@year", Sql.int dto.Year
         "@balance", Sql.money dto.Balance
         "@name", Sql.text dto.Name
         "@accountId", Sql.uuid dto.AccountId
      ]

   return statement
}

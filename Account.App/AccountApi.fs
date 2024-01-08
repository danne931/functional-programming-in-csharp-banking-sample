module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open FsToolkit.ErrorHandling
open Validus

open Lib.Types
open Lib.Postgres
open Bank.Account.Domain
open Bank.Transfer.Domain

let processCommand
   (system: ActorSystem)
   (command: AccountCommand)
   (entityId: Guid)
   (validation: ValidationResult<BankEvent<'E>>)
   =
   taskResult {
      let! _ = Result.mapError ValidationError validation
      let ref = AccountActor.get system entityId
      ref <! AccountMessage.StateChange command
      return validation
   }

let getAccountEvents
   (sys: ActorSystem)
   (accountId: Guid)
   : AccountEvent list option Task
   =
   let ref = AccountActor.get sys accountId
   ref <? AccountMessage.GetEvents |> Async.toTask

let getAccount (sys: ActorSystem) (accountId: Guid) : AccountState option Task =
   let ref = AccountActor.get sys accountId
   ref <? AccountMessage.GetAccount |> Async.toTask

let getAccounts () =
   pgQuery<AccountState> "SELECT * FROM accounts" None
   <| fun (read: RowReader) -> {
      EntityId = read.uuid "id"
      Email = read.text "email" |> Email.deserialize
      FirstName = read.text "first_name"
      LastName = read.text "last_name"
      Currency =
         read.text "currency"
         |> sprintf "\"%s\""
         |> Serialization.deserializeUnsafe<Currency>
      Status =
         read.text "status"
         |> sprintf "\"%s\""
         |> Serialization.deserializeUnsafe<AccountStatus>
      Balance = read.decimal "balance"
      DailyDebitLimit = read.decimal "daily_debit_limit"
      DailyDebitAccrued = read.decimal "daily_debit_accrued"
      LastDebitDate = read.dateTimeOrNone "last_debit_date"
      TransferRecipients =
         read.string "transfer_recipients"
         |> Serialization.deserializeUnsafe<Map<string, TransferRecipient>>
      MaintenanceFeeCriteria = {
         QualifyingDepositFound =
            read.bool "maintenance_fee_qualifying_deposit_found"
         DailyBalanceThreshold =
            read.bool "maintenance_fee_daily_balance_threshold"
      }
   }

let upsertAccounts (accounts: AccountState list) =
   let sqlParams =
      accounts
      |> List.map (fun account -> [
         "@id", Sql.uuid account.EntityId
         "@email", Sql.text <| string account.Email
         "@firstName", Sql.text account.FirstName
         "@lastName", Sql.text account.LastName
         "@balance", Sql.money account.Balance
         "@currency", Sql.string <| string account.Currency
         "@status", Sql.string <| string account.Status
         "@dailyDebitLimit", Sql.decimal <| account.DailyDebitLimit
         "@dailyDebitAccrued", Sql.decimal <| account.DailyDebitAccrued
         "@lastDebitDate", Sql.dateOrNone account.LastDebitDate
         "@transferRecipients",
         Sql.jsonb <| Serialization.serialize account.TransferRecipients
         "@maintenanceFeeQualifyingDepositFound",
         Sql.bool account.MaintenanceFeeCriteria.QualifyingDepositFound
         "@maintenanceFeeDailyBalanceThreshold",
         Sql.bool account.MaintenanceFeeCriteria.DailyBalanceThreshold
      ])

   pgTransaction [
      """
      INSERT into accounts
         (id,
          email,
          first_name,
          last_name,
          balance,
          currency,
          status,
          daily_debit_limit,
          daily_debit_accrued,
          last_debit_date,
          transfer_recipients,
          maintenance_fee_qualifying_deposit_found,
          maintenance_fee_daily_balance_threshold)
      VALUES
         (@id,
          @email,
          @firstName,
          @lastName,
          @balance,
          @currency,
          @status,
          @dailyDebitLimit,
          @dailyDebitAccrued,
          @lastDebitDate,
          @transferRecipients,
          @maintenanceFeeQualifyingDepositFound,
          @maintenanceFeeDailyBalanceThreshold)
      ON CONFLICT (id)
      DO UPDATE SET
         balance = @balance,
         status = @status,
         daily_debit_limit = @dailyDebitLimit,
         daily_debit_accrued = @dailyDebitAccrued,
         last_debit_date = @lastDebitDate,
         transfer_recipients = @transferRecipients,
         maintenance_fee_qualifying_deposit_found = @maintenanceFeeQualifyingDepositFound,
         maintenance_fee_daily_balance_threshold = @maintenanceFeeDailyBalanceThreshold;
      """,
      sqlParams
   ]

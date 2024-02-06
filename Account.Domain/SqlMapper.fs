module AccountSqlMapper

open System
open Npgsql.FSharp

open Lib.Types
open MaintenanceFee
open Bank.Account.Domain
open Bank.Transfer.Domain

module AccountFields =
   let entityId = "id"
   let email = "email"
   let firstName = "first_name"
   let lastName = "last_name"
   let currency = "currency"
   let status = "status"
   let balance = "balance"
   let dailyDebitLimit = "daily_debit_limit"
   let dailyDebitAccrued = "daily_debit_accrued"
   let lastDebitDate = "last_debit_at"
   let lastBillingCycleDate = "last_billing_cycle_at"
   let transferRecipients = "transfer_recipients"
   let events = "events"

   let maintenanceFeeQualifyingDepositFound =
      "maintenance_fee_qualifying_deposit_found"

   let maintenanceFeeDailyBalanceThreshold =
      "maintenance_fee_daily_balance_threshold"

   let inProgressTransfers = "in_progress_transfers"
   let inProgressTransfersCount = "in_progress_transfers_count"
   let cardLocked = "card_locked"

module AccountSqlReader =
   let entityId (read: RowReader) = read.uuid AccountFields.entityId

   let email (read: RowReader) =
      read.string AccountFields.email |> Email.deserialize

   let firstName (read: RowReader) = read.string AccountFields.firstName
   let lastName (read: RowReader) = read.string AccountFields.lastName

   let currency (read: RowReader) =
      read.string AccountFields.currency
      |> sprintf "\"%s\""
      |> Serialization.deserializeUnsafe<Currency>

   let status (read: RowReader) =
      read.string AccountFields.status
      |> sprintf "\"%s\""
      |> Serialization.deserializeUnsafe<AccountStatus>

   let balance (read: RowReader) = read.decimal AccountFields.balance

   let dailyDebitLimit (read: RowReader) =
      read.decimal AccountFields.dailyDebitLimit

   let dailyDebitAccrued (read: RowReader) =
      read.decimal AccountFields.dailyDebitAccrued

   let lastDebitDate (read: RowReader) =
      read.dateTimeOrNone AccountFields.lastDebitDate

   let lastBillingCycleDate (read: RowReader) =
      read.dateTimeOrNone AccountFields.lastBillingCycleDate

   let transferRecipients (read: RowReader) =
      read.text AccountFields.transferRecipients
      |> Serialization.deserializeUnsafe<TransferRecipient list>

   let maintenanceFeeCriteria (read: RowReader) = {
      QualifyingDepositFound =
         read.bool AccountFields.maintenanceFeeQualifyingDepositFound
      DailyBalanceThreshold =
         read.bool AccountFields.maintenanceFeeDailyBalanceThreshold
   }

   let events (read: RowReader) =
      read.text AccountFields.events
      |> Serialization.deserializeUnsafe<AccountEvent list>

   let inProgressTransfers (read: RowReader) =
      read.text AccountFields.inProgressTransfers
      |> Serialization.deserializeUnsafe<TransferTransaction list>

   let cardLocked (read: RowReader) = read.bool AccountFields.cardLocked

   let account (read: RowReader) : AccountState = {
      EntityId = entityId read
      Email = email read
      FirstName = firstName read
      LastName = lastName read
      Currency = currency read
      Status = status read
      Balance = balance read
      DailyDebitLimit = dailyDebitLimit read
      DailyDebitAccrued = dailyDebitAccrued read
      LastDebitDate = lastDebitDate read
      LastBillingCycleDate = lastBillingCycleDate read
      TransferRecipients =
         transferRecipients read
         |> List.map (fun o -> Account.recipientLookupKey o, o)
         |> Map.ofList
      MaintenanceFeeCriteria = maintenanceFeeCriteria read
      Events = events read
      InProgressTransfers =
         inProgressTransfers read
         |> List.map (fun txn -> string txn.TransactionId, txn)
         |> Map.ofList
      CardLocked = cardLocked read
   }

module AccountSqlWriter =
   let entityId = Sql.uuid
   let email (email: Email) = Sql.string <| string email
   let firstName = Sql.string
   let lastName = Sql.string
   let balance = Sql.money
   let currency (currency: Currency) = Sql.string <| string currency
   let status (status: AccountStatus) = Sql.string <| string status
   let dailyDebitLimit = Sql.decimal
   let dailyDebitAccrued = Sql.decimal
   let lastDebitDate (date: DateTime option) = Sql.timestamptzOrNone date
   let lastBillingCycleDate (date: DateTime option) = Sql.timestamptzOrNone date

   let transferRecipients (recipients: Map<string, TransferRecipient>) =
      Sql.jsonb <| Serialization.serialize recipients.Values

   let events (events: AccountEvent list) =
      Sql.jsonb <| Serialization.serialize events

   let maintenanceFeeQualifyingDepositFound = Sql.bool
   let maintenanceFeeDailyBalanceThreshold = Sql.bool

   let inProgressTransfers
      (inProgressTransfers: Map<string, TransferTransaction>)
      =
      Sql.jsonb <| Serialization.serialize inProgressTransfers.Values

   let inProgressTransfersCount = Sql.int
   let cardLocked = Sql.bool

module AccountSqlMapper

open System

open Lib.SharedTypes
open MaintenanceFee
open Bank.Account.Domain
open Bank.Transfer.Domain
open OrganizationSqlMapper

let table = "account"

module AccountFields =
   let accountId = "account_id"
   let orgId = OrgFields.orgId
   let email = "email"
   let firstName = "first_name"
   let lastName = "last_name"
   let currency = "currency"
   let status = "status"
   let balance = "balance"
   let dailyDebitLimit = "daily_debit_limit"
   let dailyDebitAccrued = "daily_debit_accrued"
   let dailyInternalTransferAccrued = "daily_internal_transfer_accrued"
   let dailyDomesticTransferAccrued = "daily_domestic_transfer_accrued"
   let lastDebitDate = "last_debit_at"
   let lastInternalTransferDate = "last_internal_transfer_at"
   let lastDomesticTransferDate = "last_domestic_transfer_at"
   let lastBillingCycleDate = "last_billing_cycle_at"
   let internalTransferRecipients = "internal_transfer_recipients"
   let domesticTransferRecipients = "domestic_transfer_recipients"
   let internalTransferSenders = "internal_transfer_senders"
   let events = "events"

   let maintenanceFeeQualifyingDepositFound =
      "maintenance_fee_qualifying_deposit_found"

   let maintenanceFeeDailyBalanceThreshold =
      "maintenance_fee_daily_balance_threshold"

   let inProgressInternalTransfers = "in_progress_internal_transfers"
   let inProgressInternalTransfersCount = "in_progress_internal_transfers_count"

   let inProgressDomesticTransfers = "in_progress_domestic_transfers"
   let inProgressDomesticTransfersCount = "in_progress_domestic_transfers_count"

   let cardLocked = "card_locked"

module AccountSqlReader =
   let accountId (read: RowReader) =
      AccountFields.accountId |> read.uuid |> AccountId

   let orgId = OrgSqlReader.orgId

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

   let dailyInternalTransferAccrued (read: RowReader) =
      read.decimal AccountFields.dailyInternalTransferAccrued

   let dailyDomesticTransferAccrued (read: RowReader) =
      read.decimal AccountFields.dailyDomesticTransferAccrued

   let lastDebitDate (read: RowReader) =
      read.dateTimeOrNone AccountFields.lastDebitDate

   let lastInternalTransferDate (read: RowReader) =
      read.dateTimeOrNone AccountFields.lastInternalTransferDate

   let lastDomesticTransferDate (read: RowReader) =
      read.dateTimeOrNone AccountFields.lastDomesticTransferDate

   let lastBillingCycleDate (read: RowReader) =
      read.dateTimeOrNone AccountFields.lastBillingCycleDate

   let internalTransferRecipients (read: RowReader) =
      read.text AccountFields.internalTransferRecipients
      |> Serialization.deserializeUnsafe<InternalTransferRecipient list>

   let domesticTransferRecipients (read: RowReader) =
      read.text AccountFields.domesticTransferRecipients
      |> Serialization.deserializeUnsafe<DomesticTransferRecipient list>

   let internalTransferSenders (read: RowReader) =
      read.text AccountFields.internalTransferSenders
      |> Serialization.deserializeUnsafe<InternalTransferSender list>

   let maintenanceFeeCriteria (read: RowReader) = {
      QualifyingDepositFound =
         read.bool AccountFields.maintenanceFeeQualifyingDepositFound
      DailyBalanceThreshold =
         read.bool AccountFields.maintenanceFeeDailyBalanceThreshold
   }

   let events (read: RowReader) =
      read.text AccountFields.events
      |> Serialization.deserializeUnsafe<AccountEvent list>

   let inProgressInternalTransfers (read: RowReader) =
      read.text AccountFields.inProgressInternalTransfers
      |> Serialization.deserializeUnsafe<BankEvent<InternalTransferPending> list>

   let inProgressDomesticTransfers (read: RowReader) =
      read.text AccountFields.inProgressDomesticTransfers
      |> Serialization.deserializeUnsafe<DomesticTransfer list>

   let cardLocked (read: RowReader) = read.bool AccountFields.cardLocked

   let account (read: RowReader) : Account = {
      AccountId = accountId read
      OrgId = orgId read
      Email = email read
      FirstName = firstName read
      LastName = lastName read
      Currency = currency read
      Status = status read
      Balance = balance read
      DailyDebitLimit = dailyDebitLimit read
      DailyDebitAccrued = dailyDebitAccrued read
      DailyInternalTransferAccrued = dailyInternalTransferAccrued read
      DailyDomesticTransferAccrued = dailyDomesticTransferAccrued read
      LastDebitDate = lastDebitDate read
      LastInternalTransferDate = lastInternalTransferDate read
      LastDomesticTransferDate = lastDomesticTransferDate read
      LastBillingCycleDate = lastBillingCycleDate read
      InternalTransferRecipients =
         internalTransferRecipients read
         |> List.map (fun recipient -> recipient.AccountId, recipient)
         |> Map.ofList
      DomesticTransferRecipients =
         domesticTransferRecipients read
         |> List.map (fun recipient -> recipient.VirtualId, recipient)
         |> Map.ofList
      InternalTransferSenders =
         internalTransferSenders read
         |> List.map (fun o -> o.AccountId, o)
         |> Map.ofList
      MaintenanceFeeCriteria = maintenanceFeeCriteria read
      Events = events read
      InProgressInternalTransfers =
         inProgressInternalTransfers read
         |> List.map (fun evt -> evt.CorrelationId, evt)
         |> Map.ofList
      InProgressDomesticTransfers =
         inProgressDomesticTransfers read
         |> List.map (fun txn -> txn.TransferId, txn)
         |> Map.ofList
      CardLocked = cardLocked read
   }

module AccountSqlWriter =
   let accountId = AccountId.get >> Sql.uuid
   let orgId = OrgSqlWriter.orgId
   let email (email: Email) = Sql.string <| string email
   let firstName = Sql.string
   let lastName = Sql.string
   let balance = Sql.money
   let currency (currency: Currency) = Sql.string <| string currency
   let status (status: AccountStatus) = Sql.string <| string status
   let dailyDebitLimit = Sql.decimal
   let dailyDebitAccrued = Sql.decimal
   let dailyInternalTransferAccrued = Sql.decimal
   let dailyDomesticTransferAccrued = Sql.decimal
   let lastDebitDate (date: DateTime option) = Sql.timestamptzOrNone date

   let lastInternalTransferDate (date: DateTime option) =
      Sql.timestamptzOrNone date

   let lastDomesticTransferDate (date: DateTime option) =
      Sql.timestamptzOrNone date

   let lastBillingCycleDate (date: DateTime option) = Sql.timestamptzOrNone date

   let internalTransferRecipients
      (recipients: Map<AccountId, InternalTransferRecipient>)
      =
      recipients.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let domesticTransferRecipients
      (recipients: Map<AccountId, DomesticTransferRecipient>)
      =
      recipients.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let internalTransferSenders
      (senders: Map<AccountId, InternalTransferSender>)
      =
      senders.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let events (events: AccountEvent list) =
      Sql.jsonb <| Serialization.serialize events

   let maintenanceFeeQualifyingDepositFound = Sql.bool
   let maintenanceFeeDailyBalanceThreshold = Sql.bool

   let inProgressInternalTransfers
      (transfers: Map<CorrelationId, BankEvent<InternalTransferPending>>)
      =
      transfers.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let inProgressDomesticTransfers
      (transfers: Map<CorrelationId, DomesticTransfer>)
      =
      transfers.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let inProgressTransfersCount = Sql.int
   let cardLocked = Sql.bool

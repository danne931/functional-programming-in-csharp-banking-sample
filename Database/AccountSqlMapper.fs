module AccountSqlMapper

open System

open Lib.SharedTypes
open MaintenanceFee
open Bank.Account.Domain
open Bank.Transfer.Domain
open OrganizationSqlMapper

let table = "account"

module AccountTypeCast =
   let depository = "account_depository"
   let status = "account_status"

module AccountFields =
   let accountId = "account_id"
   let orgId = OrgFields.orgId
   let accountNumber = "account_number"
   let routingNumber = "routing_number"
   let name = "account_name"
   let depository = "depository"
   let currency = "currency"
   let status = "status"
   let balance = "balance"
   let lastBillingCycleDate = "last_billing_cycle_at"
   let internalTransferRecipients = "internal_transfer_recipients"
   let domesticTransferRecipients = "domestic_transfer_recipients"
   let internalTransferSenders = "internal_transfer_senders"

   let maintenanceFeeQualifyingDepositFound =
      "maintenance_fee_qualifying_deposit_found"

   let maintenanceFeeDailyBalanceThreshold =
      "maintenance_fee_daily_balance_threshold"

   let inProgressInternalTransfers = "in_progress_internal_transfers"
   let inProgressInternalTransfersCount = "in_progress_internal_transfers_count"

   let inProgressDomesticTransfers = "in_progress_domestic_transfers"
   let inProgressDomesticTransfersCount = "in_progress_domestic_transfers_count"

   let failedDomesticTransfers = "failed_domestic_transfers"
   let failedDomesticTransfersCount = "failed_domestic_transfers_count"

module AccountSqlReader =
   let accountId (read: RowReader) =
      AccountFields.accountId |> read.uuid |> AccountId

   let orgId = OrgSqlReader.orgId

   let accountNumber (read: RowReader) =
      read.int64 AccountFields.accountNumber |> AccountNumber

   let routingNumber (read: RowReader) =
      read.int AccountFields.routingNumber |> RoutingNumber

   let name (read: RowReader) = read.string AccountFields.name

   let depository (read: RowReader) =
      read.string AccountFields.depository |> AccountDepository.fromStringUnsafe

   let currency (read: RowReader) =
      read.string AccountFields.currency
      |> sprintf "\"%s\""
      |> Serialization.deserializeUnsafe<Currency>

   let status (read: RowReader) =
      read.string AccountFields.status |> AccountStatus.fromStringUnsafe

   let balance (read: RowReader) = read.decimal AccountFields.balance

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

   let inProgressInternalTransfers (read: RowReader) =
      read.text AccountFields.inProgressInternalTransfers
      |> Serialization.deserializeUnsafe<BankEvent<InternalTransferPending> list>

   let inProgressDomesticTransfers (read: RowReader) =
      read.text AccountFields.inProgressDomesticTransfers
      |> Serialization.deserializeUnsafe<DomesticTransfer list>

   let failedDomesticTransfers (read: RowReader) =
      read.text AccountFields.failedDomesticTransfers
      |> Serialization.deserializeUnsafe<DomesticTransfer list>

   let account (read: RowReader) : Account = {
      AccountId = accountId read
      OrgId = orgId read
      AccountNumber = accountNumber read
      RoutingNumber = routingNumber read
      Name = name read
      Depository = depository read
      Currency = currency read
      Status = status read
      Balance = balance read
      LastBillingCycleDate = lastBillingCycleDate read
      InternalTransferRecipients =
         internalTransferRecipients read
         |> List.map (fun recipient -> recipient.AccountId, recipient)
         |> Map.ofList
      DomesticTransferRecipients =
         domesticTransferRecipients read
         |> List.map (fun recipient -> recipient.AccountId, recipient)
         |> Map.ofList
      InternalTransferSenders =
         internalTransferSenders read
         |> List.map (fun o -> o.AccountId, o)
         |> Map.ofList
      MaintenanceFeeCriteria = maintenanceFeeCriteria read
      InProgressInternalTransfers =
         inProgressInternalTransfers read
         |> List.map (fun evt -> evt.CorrelationId, evt)
         |> Map.ofList
      InProgressDomesticTransfers =
         inProgressDomesticTransfers read
         |> List.map (fun txn -> txn.TransferId, txn)
         |> Map.ofList
      FailedDomesticTransfers =
         failedDomesticTransfers read
         |> List.map (fun txn -> txn.TransferId, txn)
         |> Map.ofList
   }

module AccountSqlWriter =
   let accountId = AccountId.get >> Sql.uuid
   let orgId = OrgSqlWriter.orgId

   let accountNumber (num: AccountNumber) =
      let (AccountNumber acctNum) = num
      Sql.int64 acctNum

   let routingNumber (num: RoutingNumber) =
      let (RoutingNumber routingNum) = num
      Sql.int routingNum

   let depository (dep: AccountDepository) = dep |> string |> Sql.string

   let name = Sql.string
   let balance = Sql.money
   let currency (currency: Currency) = Sql.string <| string currency

   let status (status: AccountStatus) = status |> string |> Sql.string

   let dailyInternalTransferAccrued = Sql.decimal
   let dailyDomesticTransferAccrued = Sql.decimal

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

   let maintenanceFeeQualifyingDepositFound = Sql.bool
   let maintenanceFeeDailyBalanceThreshold = Sql.bool

   let inProgressInternalTransfers
      (transfers: Map<CorrelationId, BankEvent<InternalTransferPending>>)
      =
      transfers.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let domesticTransfers (transfers: Map<CorrelationId, DomesticTransfer>) =
      transfers.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let transfersCount = Sql.int

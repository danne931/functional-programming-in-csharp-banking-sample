module AccountSqlMapper

open System

open Lib.SharedTypes
open MaintenanceFee
open Bank.Account.Domain
open Bank.Transfer.Domain
open OrganizationSqlMapper
open AutomaticTransfer

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
   let domesticTransferRecipients = "domestic_transfer_recipients"

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

   let autoTransferRules = "auto_transfer_rules"

   let autoTransferRuleCounts = {|
      perTxn = "auto_transfer_rules_per_transaction_count"
      daily = "auto_transfer_rules_daily_count"
      twiceMonthly = "auto_transfer_rules_twice_monthly_count"
   |}

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

   let domesticTransferRecipients (read: RowReader) =
      read.text AccountFields.domesticTransferRecipients
      |> Serialization.deserializeUnsafe<DomesticTransferRecipient list>

   let maintenanceFeeCriteria (read: RowReader) = {
      QualifyingDepositFound =
         read.bool AccountFields.maintenanceFeeQualifyingDepositFound
      DailyBalanceThreshold =
         read.bool AccountFields.maintenanceFeeDailyBalanceThreshold
   }

   let inProgressInternalTransfers
      (read: RowReader)
      : InProgressInternalTransfer list
      =
      read.text AccountFields.inProgressInternalTransfers
      |> Serialization.deserializeUnsafe<InProgressInternalTransfer list>

   let inProgressDomesticTransfers (read: RowReader) =
      read.text AccountFields.inProgressDomesticTransfers
      |> Serialization.deserializeUnsafe<DomesticTransfer list>

   let failedDomesticTransfers (read: RowReader) =
      read.text AccountFields.failedDomesticTransfers
      |> Serialization.deserializeUnsafe<DomesticTransfer list>

   let autoTransferRules (read: RowReader) =
      read.text AccountFields.autoTransferRules
      |> Serialization.deserializeUnsafe<AutomaticTransferConfig list>

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
      DomesticTransferRecipients =
         domesticTransferRecipients read
         |> List.map (fun recipient -> recipient.AccountId, recipient)
         |> Map.ofList
      MaintenanceFeeCriteria = maintenanceFeeCriteria read
      InProgressInternalTransfers =
         inProgressInternalTransfers read
         |> List.map (fun t -> t.TransferId, t)
         |> Map.ofList
      InProgressDomesticTransfers =
         inProgressDomesticTransfers read
         |> List.map (fun t -> t.TransferId, t)
         |> Map.ofList
      FailedDomesticTransfers =
         failedDomesticTransfers read
         |> List.map (fun t -> t.TransferId, t)
         |> Map.ofList
      AutoTransferRules =
         autoTransferRules read |> List.map (fun r -> r.Id, r) |> Map.ofList
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

   let domesticTransferRecipients
      (recipients: Map<AccountId, DomesticTransferRecipient>)
      =
      recipients.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let maintenanceFeeQualifyingDepositFound = Sql.bool
   let maintenanceFeeDailyBalanceThreshold = Sql.bool

   let inProgressInternalTransfers
      (transfers: Map<TransferId, InProgressInternalTransfer>)
      =
      transfers.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let domesticTransfers (transfers: Map<TransferId, DomesticTransfer>) =
      transfers.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let transfersCount = Sql.int

   let autoTransferRules (rules: Map<Guid, AutomaticTransferConfig>) =
      rules.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let autoTransferRuleCount = Sql.int

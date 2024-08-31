[<RequireQualifiedAccess>]
module AccountReadModelSyncActor

open System
open Akkling
open Akkling.Cluster.Sharding

open Lib.SharedTypes
open Lib.Types
open Lib.Postgres
open Bank.Account.Domain
open AccountSqlMapper
open TransactionSqlMapper
open Lib.ReadModelSyncActor

let upsertReadModels
   (accounts: Account list, accountEvents: AccountEvent list)
   =
   let accountSqlParams =
      accounts
      |> List.map (fun account -> [
         "id", AccountSqlWriter.accountId account.AccountId
         "accountNumber", AccountSqlWriter.accountNumber account.AccountNumber
         "routingNumber", AccountSqlWriter.routingNumber account.RoutingNumber
         "orgId", AccountSqlWriter.orgId account.OrgId
         "name", AccountSqlWriter.name account.Name
         "depository", AccountSqlWriter.depository account.Depository
         "balance", AccountSqlWriter.balance account.Balance
         "currency", AccountSqlWriter.currency account.Currency
         "status", AccountSqlWriter.status account.Status
         "lastBillingCycleDate",
         AccountSqlWriter.lastBillingCycleDate account.LastBillingCycleDate

         "domesticTransferRecipients",
         AccountSqlWriter.domesticTransferRecipients
            account.DomesticTransferRecipients

         "maintenanceFeeQualifyingDepositFound",
         AccountSqlWriter.maintenanceFeeQualifyingDepositFound
            account.MaintenanceFeeCriteria.QualifyingDepositFound

         "maintenanceFeeDailyBalanceThreshold",
         AccountSqlWriter.maintenanceFeeDailyBalanceThreshold
            account.MaintenanceFeeCriteria.DailyBalanceThreshold

         "inProgressInternalTransfers",
         AccountSqlWriter.inProgressInternalTransfers
            account.InProgressInternalTransfers

         "inProgressInternalTransfersCount",
         AccountSqlWriter.transfersCount
            account.InProgressInternalTransfers.Count

         "inProgressDomesticTransfers",
         AccountSqlWriter.domesticTransfers account.InProgressDomesticTransfers

         "inProgressDomesticTransfersCount",
         AccountSqlWriter.transfersCount
            account.InProgressDomesticTransfers.Count

         "failedDomesticTransfers",
         AccountSqlWriter.domesticTransfers account.FailedDomesticTransfers

         "failedDomesticTransfersCount",
         AccountSqlWriter.transfersCount account.FailedDomesticTransfers.Count
      ])

   let transactionSqlParams =
      accountEvents
      |> List.map (fun evt ->
         let evt, envelope = AccountEnvelope.unwrap evt

         let sqlParams = [
            "transactionId", TransactionSqlWriter.transactionId envelope.Id

            "accountId",
            envelope.EntityId
            |> AccountId.fromEntityId
            |> TransactionSqlWriter.accountId

            "orgId", TransactionSqlWriter.orgId envelope.OrgId

            "correlationId",
            TransactionSqlWriter.correlationId envelope.CorrelationId

            "initiatedById",
            TransactionSqlWriter.initiatedById envelope.InitiatedById

            "name", TransactionSqlWriter.name envelope.EventName
            "timestamp", TransactionSqlWriter.timestamp envelope.Timestamp
            "event", TransactionSqlWriter.event evt
         ]

         let amountOpt, moneyFlowOpt, sourceOpt =
            match evt with
            | AccountEvent.DepositedCash evt ->
               Some evt.Data.Amount, Some MoneyFlow.In, Some evt.Data.Origin
            | AccountEvent.DebitedAccount evt ->
               Some evt.Data.Amount, Some MoneyFlow.Out, Some evt.Data.Origin
            | AccountEvent.InternalTransferWithinOrgPending evt ->
               Some evt.Data.BaseInfo.Amount,
               Some MoneyFlow.Out,
               Some evt.Data.BaseInfo.RecipientName
            | AccountEvent.InternalTransferWithinOrgApproved evt ->
               Some evt.Data.BaseInfo.Amount,
               None,
               Some evt.Data.BaseInfo.RecipientName
            | AccountEvent.InternalTransferWithinOrgRejected evt ->
               Some evt.Data.BaseInfo.Amount,
               Some MoneyFlow.In,
               Some evt.Data.BaseInfo.RecipientName
            | AccountEvent.InternalTransferWithinOrgDeposited evt ->
               Some evt.Data.Amount,
               Some MoneyFlow.In,
               Some evt.Data.Source.Name
            | AccountEvent.InternalTransferBetweenOrgsPending evt ->
               Some evt.Data.BaseInfo.Amount,
               Some MoneyFlow.Out,
               Some evt.Data.BaseInfo.RecipientName
            | AccountEvent.InternalTransferBetweenOrgsApproved evt ->
               Some evt.Data.BaseInfo.Amount,
               None,
               Some evt.Data.BaseInfo.RecipientName
            | AccountEvent.InternalTransferBetweenOrgsRejected evt ->
               Some evt.Data.BaseInfo.Amount,
               Some MoneyFlow.In,
               Some evt.Data.BaseInfo.RecipientName
            | AccountEvent.InternalTransferBetweenOrgsDeposited evt ->
               Some evt.Data.Amount,
               Some MoneyFlow.In,
               Some evt.Data.Source.Name
            | AccountEvent.DomesticTransferPending evt ->
               Some evt.Data.BaseInfo.Amount,
               Some MoneyFlow.Out,
               Some evt.Data.BaseInfo.Recipient.Name
            | AccountEvent.DomesticTransferRejected evt ->
               Some evt.Data.BaseInfo.Amount,
               Some MoneyFlow.In,
               Some evt.Data.BaseInfo.Recipient.Name
            | AccountEvent.MaintenanceFeeDebited evt ->
               Some evt.Data.Amount, Some MoneyFlow.Out, Some "Maintenance Fee"
            | _ -> None, None, None

         let cardIdOpt =
            match evt with
            | AccountEvent.DebitedAccount e ->
               Some e.Data.EmployeePurchaseReference.CardId
            | _ -> None

         sqlParams
         @ [
            "amount", TransactionSqlWriter.amount amountOpt
            "moneyFlow", TransactionSqlWriter.moneyFlow moneyFlowOpt
            "source", TransactionSqlWriter.source sourceOpt
            "cardId", TransactionSqlWriter.cardId cardIdOpt
         ])

   pgTransaction [
      $"""
      INSERT into {AccountSqlMapper.table}
         ({AccountFields.accountId},
          {AccountFields.accountNumber},
          {AccountFields.routingNumber},
          {AccountFields.orgId},
          {AccountFields.name},
          {AccountFields.depository},
          {AccountFields.balance},
          {AccountFields.currency},
          {AccountFields.status},
          {AccountFields.lastBillingCycleDate},
          {AccountFields.domesticTransferRecipients},
          {AccountFields.maintenanceFeeQualifyingDepositFound},
          {AccountFields.maintenanceFeeDailyBalanceThreshold},
          {AccountFields.inProgressInternalTransfers},
          {AccountFields.inProgressInternalTransfersCount},
          {AccountFields.inProgressDomesticTransfers},
          {AccountFields.inProgressDomesticTransfersCount},
          {AccountFields.failedDomesticTransfers},
          {AccountFields.failedDomesticTransfersCount})
      VALUES
         (@id,
          @accountNumber,
          @routingNumber,
          @orgId,
          @name,
          @depository::{AccountTypeCast.depository},
          @balance,
          @currency,
          @status::{AccountTypeCast.status},
          @lastBillingCycleDate,
          @domesticTransferRecipients,
          @maintenanceFeeQualifyingDepositFound,
          @maintenanceFeeDailyBalanceThreshold,
          @inProgressInternalTransfers,
          @inProgressInternalTransfersCount,
          @inProgressDomesticTransfers,
          @inProgressDomesticTransfersCount,
          @failedDomesticTransfers,
          @failedDomesticTransfersCount)
      ON CONFLICT ({AccountFields.accountId})
      DO UPDATE SET
         {AccountFields.balance} = @balance,
         {AccountFields.status} = @status::{AccountTypeCast.status},
         {AccountFields.lastBillingCycleDate} = @lastBillingCycleDate,
         {AccountFields.domesticTransferRecipients} = @domesticTransferRecipients,
         {AccountFields.maintenanceFeeQualifyingDepositFound} = @maintenanceFeeQualifyingDepositFound,
         {AccountFields.maintenanceFeeDailyBalanceThreshold} = @maintenanceFeeDailyBalanceThreshold,
         {AccountFields.inProgressInternalTransfers} = @inProgressInternalTransfers,
         {AccountFields.inProgressInternalTransfersCount} = @inProgressInternalTransfersCount,
         {AccountFields.inProgressDomesticTransfers} = @inProgressDomesticTransfers,
         {AccountFields.inProgressDomesticTransfersCount} = @inProgressDomesticTransfersCount,
         {AccountFields.failedDomesticTransfers} = @failedDomesticTransfers,
         {AccountFields.failedDomesticTransfersCount} = @failedDomesticTransfersCount;
      """,
      accountSqlParams

      $"""
      INSERT into {TransactionSqlMapper.table}
         ({TransactionFields.transactionId},
          {TransactionFields.accountId},
          {TransactionFields.orgId},
          {TransactionFields.correlationId},
          {TransactionFields.initiatedById},
          {TransactionFields.cardId},
          {TransactionFields.name},
          {TransactionFields.timestamp},
          {TransactionFields.event},
          {TransactionFields.amount},
          {TransactionFields.source},
          {TransactionFields.moneyFlow})
      VALUES
         (@transactionId,
          @accountId,
          @orgId,
          @correlationId,
          @initiatedById,
          @cardId,
          @name,
          @timestamp,
          @event,
          @amount,
          @source,
          @moneyFlow::{TransactionTypeCast.moneyFlow})
      ON CONFLICT ({TransactionFields.transactionId})
      DO NOTHING;
      """,
      transactionSqlParams
   ]

let initProps
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryPersistenceAfter: TimeSpan)
   =
   actorProps<Account, AccountEvent> (
      {
         GetAggregateIdFromEvent =
            AccountEnvelope.unwrap >> snd >> _.EntityId >> EntityId.get
         GetAggregate =
            fun accountId -> task {
               let aref = getAccountRef (AccountId accountId)

               let! (accountOpt: Account option) =
                  aref <? AccountMessage.GetAccount

               return accountOpt
            }
         Chunking = chunking
         RestartSettings = restartSettings
         RetryPersistenceAfter = retryPersistenceAfter
         UpsertReadModels = upsertReadModels
         EventJournalTag = Constants.AKKA_ACCOUNT_JOURNAL
      }
   )

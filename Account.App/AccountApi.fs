module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open FsToolkit.ErrorHandling
open Validus

open Lib.SharedTypes
open Lib.Postgres
open Bank.Account.Domain
open AccountSqlMapper

let processCommand
   (system: ActorSystem)
   (command: AccountCommand)
   (entityId: Guid)
   (validation: ValidationResult<BankEvent<'E>>)
   =
   taskResult {
      let! _ = validation |> Result.mapError Err.ValidationError
      let ref = AccountActor.get system entityId
      ref <! AccountMessage.StateChange command
      return validation |> Result.map _.Id
   }

let getAccount (id: Guid) = taskResultOption {
   let! accountList =
      pgQuery<AccountState>
         "SELECT * FROM accounts WHERE id = @accountId"
         (Some [ "accountId", Sql.uuid id ])
         AccountSqlReader.account

   return accountList.Head
}

let getAccounts () =
   pgQuery<AccountState> "SELECT * FROM accounts" None AccountSqlReader.account

let getAccountsByIds (accountIds: Guid list) =
   pgQuery<AccountState>
      "SELECT * FROM accounts WHERE id = ANY(@accountIds)"
      (Some [ "accountIds", accountIds |> List.toArray |> Sql.uuidArray ])
      AccountSqlReader.account

let upsertAccounts (accounts: AccountState list) =
   let sqlParams =
      accounts
      |> List.map (fun account -> [
         "@id", AccountSqlWriter.entityId account.EntityId
         "@email", AccountSqlWriter.email account.Email
         "@firstName", AccountSqlWriter.firstName account.FirstName
         "@lastName", AccountSqlWriter.lastName account.LastName
         "@balance", AccountSqlWriter.balance account.Balance
         "@currency", AccountSqlWriter.currency account.Currency
         "@status", AccountSqlWriter.status account.Status
         "@dailyDebitLimit",
         AccountSqlWriter.dailyDebitLimit account.DailyDebitLimit
         "@dailyDebitAccrued",
         AccountSqlWriter.dailyDebitAccrued account.DailyDebitAccrued
         "@dailyInternalTransferAccrued",
         AccountSqlWriter.dailyInternalTransferAccrued
            account.DailyInternalTransferAccrued
         "@dailyDomesticTransferAccrued",
         AccountSqlWriter.dailyDomesticTransferAccrued
            account.DailyDomesticTransferAccrued
         "@lastDebitDate", AccountSqlWriter.lastDebitDate account.LastDebitDate
         "@lastInternalTransferDate",
         AccountSqlWriter.lastInternalTransferDate
            account.LastInternalTransferDate
         "@lastDomesticTransferDate",
         AccountSqlWriter.lastDomesticTransferDate
            account.LastDomesticTransferDate
         "@lastBillingCycleDate",
         AccountSqlWriter.lastBillingCycleDate account.LastBillingCycleDate
         "@transferRecipients",
         AccountSqlWriter.transferRecipients account.TransferRecipients
         "@internalTransferSenders",
         AccountSqlWriter.internalTransferSenders
            account.InternalTransferSenders
         "@events", AccountSqlWriter.events account.Events
         "@maintenanceFeeQualifyingDepositFound",
         AccountSqlWriter.maintenanceFeeQualifyingDepositFound
            account.MaintenanceFeeCriteria.QualifyingDepositFound
         "@maintenanceFeeDailyBalanceThreshold",
         AccountSqlWriter.maintenanceFeeDailyBalanceThreshold
            account.MaintenanceFeeCriteria.DailyBalanceThreshold
         "@inProgressTransfers",
         AccountSqlWriter.inProgressTransfers account.InProgressTransfers
         "@inProgressTransfersCount",
         AccountSqlWriter.inProgressTransfersCount
            account.InProgressTransfers.Count
         "@cardLocked", AccountSqlWriter.cardLocked account.CardLocked
      ])

   pgTransaction [
      $"""
      INSERT into accounts
         ({AccountFields.entityId},
          {AccountFields.email},
          {AccountFields.firstName},
          {AccountFields.lastName},
          {AccountFields.balance},
          {AccountFields.currency},
          {AccountFields.status},
          {AccountFields.dailyDebitLimit},
          {AccountFields.dailyDebitAccrued},
          {AccountFields.dailyInternalTransferAccrued},
          {AccountFields.dailyDomesticTransferAccrued},
          {AccountFields.lastDebitDate},
          {AccountFields.lastInternalTransferDate},
          {AccountFields.lastDomesticTransferDate},
          {AccountFields.lastBillingCycleDate},
          {AccountFields.transferRecipients},
          {AccountFields.internalTransferSenders},
          {AccountFields.events},
          {AccountFields.maintenanceFeeQualifyingDepositFound},
          {AccountFields.maintenanceFeeDailyBalanceThreshold},
          {AccountFields.inProgressTransfers},
          {AccountFields.inProgressTransfersCount},
          {AccountFields.cardLocked})
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
          @dailyInternalTransferAccrued,
          @dailyDomesticTransferAccrued,
          @lastDebitDate,
          @lastInternalTransferDate,
          @lastDomesticTransferDate,
          @lastBillingCycleDate,
          @transferRecipients,
          @internalTransferSenders,
          @events,
          @maintenanceFeeQualifyingDepositFound,
          @maintenanceFeeDailyBalanceThreshold,
          @inProgressTransfers,
          @inProgressTransfersCount,
          @cardLocked)
      ON CONFLICT ({AccountFields.entityId})
      DO UPDATE SET
         {AccountFields.balance} = @balance,
         {AccountFields.status} = @status,
         {AccountFields.dailyDebitLimit} = @dailyDebitLimit,
         {AccountFields.dailyDebitAccrued} = @dailyDebitAccrued,
         {AccountFields.dailyInternalTransferAccrued} = @dailyInternalTransferAccrued,
         {AccountFields.dailyDomesticTransferAccrued} = @dailyDomesticTransferAccrued,
         {AccountFields.lastDebitDate} = @lastDebitDate,
         {AccountFields.lastInternalTransferDate} = @lastInternalTransferDate,
         {AccountFields.lastDomesticTransferDate} = @lastDomesticTransferDate,
         {AccountFields.lastBillingCycleDate} = @lastBillingCycleDate,
         {AccountFields.transferRecipients} = @transferRecipients,
         {AccountFields.internalTransferSenders} = @internalTransferSenders,
         {AccountFields.events} = @events,
         {AccountFields.maintenanceFeeQualifyingDepositFound} = @maintenanceFeeQualifyingDepositFound,
         {AccountFields.maintenanceFeeDailyBalanceThreshold} = @maintenanceFeeDailyBalanceThreshold,
         {AccountFields.inProgressTransfers} = @inProgressTransfers,
         {AccountFields.inProgressTransfersCount} = @inProgressTransfersCount,
         {AccountFields.cardLocked} = @cardLocked;
      """,
      sqlParams
   ]

// Diagnostic
let getAccountEventsFromAkka
   (sys: ActorSystem)
   (accountId: Guid)
   : AccountEvent list Task
   =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetEvents, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

// Diagnostic
let getAccountFromAkka
   (sys: ActorSystem)
   (accountId: Guid)
   : AccountState option Task
   =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetAccount, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

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
open AccountSqlMapper

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

let getAccount (sys: ActorSystem) (accountId: Guid) : AccountState option Task =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetAccount, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

let getAccounts () =
   pgQuery<AccountState> "SELECT * FROM accounts" None AccountSqlReader.account

let getAccountsByIds (accountIds: Guid list) =
   pgQuery<AccountState>
      "SELECT * FROM accounts WHERE id = ANY(@accountIds)"
      (Some [ "@accountIds", accountIds |> List.toArray |> Sql.uuidArray ])
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
         "@lastDebitDate", AccountSqlWriter.lastDebitDate account.LastDebitDate
         "@transferRecipients",
         AccountSqlWriter.transferRecipients account.TransferRecipients
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
          {AccountFields.lastDebitDate},
          {AccountFields.transferRecipients},
          {AccountFields.events},
          {AccountFields.maintenanceFeeQualifyingDepositFound},
          {AccountFields.maintenanceFeeDailyBalanceThreshold},
          {AccountFields.inProgressTransfers},
          {AccountFields.inProgressTransfersCount})
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
          @events,
          @maintenanceFeeQualifyingDepositFound,
          @maintenanceFeeDailyBalanceThreshold,
          @inProgressTransfers,
          @inProgressTransfersCount)
      ON CONFLICT ({AccountFields.entityId})
      DO UPDATE SET
         {AccountFields.balance} = @balance,
         {AccountFields.status} = @status,
         {AccountFields.dailyDebitLimit} = @dailyDebitLimit,
         {AccountFields.dailyDebitAccrued} = @dailyDebitAccrued,
         {AccountFields.lastDebitDate} = @lastDebitDate,
         {AccountFields.transferRecipients} = @transferRecipients,
         {AccountFields.events} = @events,
         {AccountFields.maintenanceFeeQualifyingDepositFound} = @maintenanceFeeQualifyingDepositFound,
         {AccountFields.maintenanceFeeDailyBalanceThreshold} = @maintenanceFeeDailyBalanceThreshold,
         {AccountFields.inProgressTransfers} = @inProgressTransfers,
         {AccountFields.inProgressTransfersCount} = @inProgressTransfersCount;
      """,
      sqlParams
   ]

// Diagnostic
let getAccountEvents
   (sys: ActorSystem)
   (accountId: Guid)
   : AccountEvent list Task
   =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetEvents, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

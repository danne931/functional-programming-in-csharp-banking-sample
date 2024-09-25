module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open FsToolkit.ErrorHandling
open Validus

open Lib.Postgres
open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Transfer.Domain

module Fields = AccountSqlMapper.AccountFields
module Reader = AccountSqlMapper.AccountSqlReader
module Writer = AccountSqlMapper.AccountSqlWriter
module TypeCast = AccountSqlMapper.AccountTypeCast
let accountTable = AccountSqlMapper.table

let getAccount (id: AccountId) =
   pgQuerySingle<Account>
      $"SELECT * FROM {accountTable} 
        WHERE {Fields.accountId} = @accountId"
      (Some [ "accountId", Writer.accountId id ])
      Reader.account

let getAccountAndTransactions (txnQuery: TransactionQuery) = taskResultOption {
   let queryParams, txnQueryString =
      Bank.Transaction.Api.transactionQuery txnQuery

   let query =
      $"""
      SELECT
         {accountTable}.*,
         COALESCE(
            (
               SELECT jsonb_agg(event)
               FROM ({txnQueryString})
            ),
            '[]'::jsonb
         ) as txns
      FROM {accountTable}
      WHERE {Fields.accountId} = @accountId
      """

   return!
      pgQuerySingle<Account * AccountEvent list>
         query
         (Some queryParams)
         (fun read ->
            Reader.account read,
            read.text "txns"
            |> Serialization.deserializeUnsafe<AccountEvent list>)
}

open OrganizationSqlMapper

let getOrg (id: OrgId) =
   let query =
      $"""
      SELECT
         o.{OrgFields.orgId},
         o.{OrgFields.name},
         op.{OrgFields.requiresEmployeeInviteApproval},
         op.{OrgFields.socialTransferDiscoveryAccountId}
      FROM {OrganizationSqlMapper.table} o
      JOIN {OrganizationSqlMapper.permissionsTable} op using({OrgFields.orgId})
      WHERE {OrgFields.orgId} = @orgId
      """

   pgQuerySingle<Org>
      query
      (Some [ "orgId", OrgSqlWriter.orgId id ])
      OrgSqlReader.org

let getOrgAndAccountProfiles
   (orgId: OrgId)
   : Task<Result<Option<OrgWithAccountProfiles>, Err>>
   =
   taskResultOption {
      let dpaView = TransactionSqlMapper.TransactionViews.dailyPurchaseAccrued
      let mpaView = TransactionSqlMapper.TransactionViews.monthlyPurchaseAccrued

      let transferAccrued =
         TransactionSqlMapper.TransactionFunctions.transferAccrued

      let query =
         $"""
         SELECT
            o.{OrgFields.name},
            op.{OrgFields.requiresEmployeeInviteApproval},
            op.{OrgFields.socialTransferDiscoveryAccountId},
            a.*,
            dta.internal_transfer_accrued as dita,
            dta.domestic_transfer_accrued as dida,
            mta.internal_transfer_accrued as mita,
            mta.domestic_transfer_accrued as mida,
            {mpaView}.amount_accrued as mpa,
            {dpaView}.amount_accrued as dpa
         FROM {OrganizationSqlMapper.table} o
         JOIN {OrganizationSqlMapper.permissionsTable} op using({OrgFields.orgId})
         JOIN {accountTable} a using({OrgFields.orgId})
         LEFT JOIN (SELECT * FROM {transferAccrued}(@orgId, 'day')) dta using({Fields.accountId})
         LEFT JOIN (SELECT * FROM {transferAccrued}(@orgId, 'month')) mta using({Fields.accountId})
         LEFT JOIN {dpaView} using({Fields.accountId})
         LEFT JOIN {mpaView} using({Fields.accountId})
         WHERE {Fields.orgId} = @orgId
         """

      let! res =
         pgQuery<Org * AccountProfile>
            query
            (Some [ "orgId", Writer.orgId orgId ])
            (fun read ->
               OrgSqlReader.org read,
               {
                  Account = Reader.account read
                  Metrics = {
                     DailyInternalTransferAccrued =
                        read.decimalOrNone "dita" |> Option.defaultValue 0m
                     DailyDomesticTransferAccrued =
                        read.decimalOrNone "dida" |> Option.defaultValue 0m
                     MonthlyInternalTransferAccrued =
                        read.decimalOrNone "mita" |> Option.defaultValue 0m
                     MonthlyDomesticTransferAccrued =
                        read.decimalOrNone "mida" |> Option.defaultValue 0m
                     DailyPurchaseAccrued =
                        read.decimalOrNone "dpa" |> Option.defaultValue 0m
                     MonthlyPurchaseAccrued =
                        read.decimalOrNone "mpa" |> Option.defaultValue 0m
                  }
               })

      return {
         Org = fst (List.head res)
         AccountProfiles =
            [ for _, profile in res -> profile.Account.AccountId, profile ]
            |> Map.ofList
         Balance = res |> List.sumBy (snd >> _.Account.Balance)
      }
   }

let searchOrgTransferSocialDiscovery (fromOrgId: OrgId) (nameQuery: string) =
   let query =
      $$"""
      SELECT
         o.{{OrgFields.orgId}},
         o.{{OrgFields.name}},
         op.{{OrgFields.requiresEmployeeInviteApproval}},
         op.{{OrgFields.socialTransferDiscoveryAccountId}}
      FROM {{OrganizationSqlMapper.table}} o
      JOIN {{OrganizationSqlMapper.permissionsTable}} op using({{OrgFields.orgId}})
      WHERE 
         o.{{OrgFields.orgId}} <> @orgIdToExclude
         AND o.{{OrgFields.name}} %> @nameQuery
         AND op.{{OrgFields.socialTransferDiscoveryAccountId}} IS NOT NULL
      ORDER BY o.{{OrgFields.name}} <-> @nameQuery DESC
      """

   pgQuery<Org>
      query
      (Some [
         "orgIdToExclude", OrgSqlWriter.orgId fromOrgId
         "nameQuery", OrgSqlWriter.name nameQuery
      ])
      OrgSqlReader.org

let getAccountsByIds (accountIds: AccountId list) =
   pgQuery<Account>
      $"SELECT * FROM {accountTable} 
        WHERE {Fields.accountId} = ANY(@accountIds)"
      (Some [
         "accountIds",
         accountIds |> List.map AccountId.get |> List.toArray |> Sql.uuidArray
      ])
      Reader.account

let processCommand (system: ActorSystem) (command: AccountCommand) = taskResult {
   let validation =
      match command with
      | CreateAccount cmd ->
         CreateAccountCommand.toEvent cmd |> Result.map AccountEnvelope.get
      | DepositCash cmd ->
         DepositCashCommand.toEvent cmd |> Result.map AccountEnvelope.get
      | InternalTransfer cmd ->
         InternalTransferWithinOrgCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | ScheduleInternalTransferBetweenOrgs cmd ->
         ScheduleInternalTransferBetweenOrgsCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | InternalTransferBetweenOrgs cmd ->
         InternalTransferBetweenOrgsCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | ScheduleDomesticTransfer cmd ->
         ScheduleDomesticTransferCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | DomesticTransfer cmd ->
         DomesticTransferCommand.toEvent cmd |> Result.map AccountEnvelope.get
      | RegisterDomesticTransferRecipient cmd ->
         RegisterDomesticTransferRecipientCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | EditDomesticTransferRecipient cmd ->
         EditDomesticTransferRecipientCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | NicknameRecipient cmd ->
         NicknameRecipientCommand.toEvent cmd |> Result.map AccountEnvelope.get
      | CloseAccount cmd ->
         CloseAccountCommand.toEvent cmd |> Result.map AccountEnvelope.get
      | RequestPlatformPayment cmd ->
         RequestPlatformPaymentCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | CancelPlatformPayment cmd ->
         CancelPlatformPaymentCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | DeclinePlatformPayment cmd ->
         DeclinePlatformPaymentCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | FulfillPlatformPayment cmd ->
         FulfillPlatformPaymentCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | ConfigureAutoTransferRule cmd ->
         ConfigureAutoTransferRuleCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | DeleteAutoTransferRule cmd ->
         DeleteAutoTransferRuleCommand.toEvent cmd
         |> Result.map AccountEnvelope.get
      | cmd ->
         Error
         <| ValidationErrors.create "" [
            $"Command processing not implemented for {cmd}"
         ]

   let! res = validation |> Result.mapError Err.ValidationError
   let ref = AccountActor.get system (AccountId.fromEntityId res.EntityId)
   ref <! AccountMessage.StateChange command
   return res
}

// Diagnostic
let getAccountFromAkka
   (sys: ActorSystem)
   (accountId: AccountId)
   : Account option Task
   =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetAccount, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open FsToolkit.ErrorHandling
open Validus

open Lib.TransactionQuery
open Lib.Postgres
open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Transfer.Domain

module Fields = AccountSqlMapper.AccountFields
module Reader = AccountSqlMapper.AccountSqlReader
module Writer = AccountSqlMapper.AccountSqlWriter
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

let getAccountProfiles (orgId: OrgId) =
   let query =
      $"""
      SELECT
         {Fields.accountId},
         {Fields.orgId},
         {Fields.firstName},
         {Fields.lastName},
         {Fields.email}
      FROM {accountTable}
      WHERE {Fields.orgId} = @orgId
      """

   pgQuery<AccountProfile>
      query
      (Some [ "orgId", Writer.orgId orgId ])
      (fun read -> {
         AccountId = Reader.accountId read
         OrgId = Reader.orgId read
         Email = Reader.email read
         FirstName = Reader.firstName read
         LastName = Reader.lastName read
      })

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
   let ids (cmd: BankEvent<_>) = cmd.EntityId, cmd.Id

   let validation =
      match command with
      | CreateAccount cmd -> CreateAccountCommand.toEvent cmd |> Result.map ids
      | DepositCash cmd -> DepositCashCommand.toEvent cmd |> Result.map ids
      | Debit cmd -> DebitCommand.toEvent cmd |> Result.map ids
      | LimitDailyDebits cmd ->
         LimitDailyDebitsCommand.toEvent cmd |> Result.map ids
      | LockCard cmd -> LockCardCommand.toEvent cmd |> Result.map ids
      | UnlockCard cmd -> UnlockCardCommand.toEvent cmd |> Result.map ids
      | InternalTransfer cmd ->
         InternalTransferCommand.toEvent cmd |> Result.map ids
      | DomesticTransfer cmd ->
         DomesticTransferCommand.toEvent cmd |> Result.map ids
      | RegisterInternalTransferRecipient cmd ->
         RegisterInternalTransferRecipientCommand.toEvent cmd |> Result.map ids
      | RegisterDomesticTransferRecipient cmd ->
         RegisterDomesticTransferRecipientCommand.toEvent cmd |> Result.map ids
      | DeactivateInternalRecipient cmd ->
         DeactivateInternalRecipientCommand.toEvent cmd |> Result.map ids
      | NicknameRecipient cmd ->
         NicknameRecipientCommand.toEvent cmd |> Result.map ids
      | CloseAccount cmd -> CloseAccountCommand.toEvent cmd |> Result.map ids
      | cmd ->
         Error
         <| ValidationErrors.create "" [
            $"Command processing not implemented for {cmd}"
         ]

   let! (entityId, _) = validation |> Result.mapError Err.ValidationError
   let ref = AccountActor.get system (AccountId.fromEntityId entityId)
   ref <! AccountMessage.StateChange command
   return validation |> Result.map snd
}

// Diagnostic
let getAccountEventsFromAkka
   (sys: ActorSystem)
   (accountId: AccountId)
   : AccountEvent list Task
   =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetEvents, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

// Diagnostic
let getAccountFromAkka
   (sys: ActorSystem)
   (accountId: AccountId)
   : Account option Task
   =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetAccount, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

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

module Fields = AccountSqlMapper.AccountFields
module Reader = AccountSqlMapper.AccountSqlReader
module Writer = AccountSqlMapper.AccountSqlWriter
let accountTable = AccountSqlMapper.table

let getAccount (id: Guid) =
   pgQuerySingle<Account>
      $"SELECT * FROM {accountTable} 
        WHERE {Fields.entityId} = @accountId"
      (Some [ "accountId", Writer.entityId id ])
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
      WHERE {Fields.entityId} = @accountId
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

let getAccountProfiles () =
   let query =
      $"""
      SELECT
         {Fields.entityId},
         {Fields.firstName},
         {Fields.lastName},
         {Fields.email}
      FROM {accountTable}
      """

   pgQuery<AccountProfile> query None (fun read -> {
      EntityId = Reader.entityId read
      Email = Reader.email read
      FirstName = Reader.firstName read
      LastName = Reader.lastName read
   })

let getAccountsByIds (accountIds: Guid list) =
   pgQuery<Account>
      $"SELECT * FROM {accountTable} 
        WHERE {Fields.entityId} = ANY(@accountIds)"
      (Some [ "accountIds", accountIds |> List.toArray |> Sql.uuidArray ])
      Reader.account

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
   : Account option Task
   =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetAccount, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

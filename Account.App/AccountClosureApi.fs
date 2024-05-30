module Bank.AccountClosure.Api

open System
open FsToolkit.ErrorHandling

open Lib.Postgres
open Lib.SharedTypes
open AccountSqlMapper

// These records are held onto for reporting and legal reasons
// for 3 months following an account closure.
//
// ON CASCADE DELETE is configured (see Infrastructure/Migrations/Bank.sql).
// Deletion of an account with a given ID deletes corresponding
// user & billing statements in the same transaction.
let deleteHistoricalRecords (accountIds: Guid list) =
   if accountIds.IsEmpty then
      TaskResult.ok None
   else
      pgQuery<Email>
         $"DELETE FROM {AccountSqlMapper.table} 
           WHERE {AccountFields.entityId} = ANY(@accountIds) 
           RETURNING email"
         (Some [ "@accountIds", accountIds |> List.toArray |> Sql.uuidArray ])
         AccountSqlReader.email

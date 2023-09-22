module Bank.AccountClosure.Api

open System
open FsToolkit.ErrorHandling

open Lib.Postgres
open Lib.Types

// These records are held onto for reporting and legal reasons
// for 3 months following an account closure.
// The account closure actor schedules this deletion.
//
// ON CASCADE DELETE is configured. Deletion of a user
// with a given account ID deletes corresponding billing
// statements in the same transaction.
let deleteHistoricalRecords (accountIds: Guid list) =
   if accountIds.IsEmpty then
      TaskResult.ok None
   else
      pgQuery<Email>
         "DELETE FROM users WHERE account_id = ANY(@accountIds) RETURNING email"
         (Some [ "@accountIds", accountIds |> List.toArray |> Sql.uuidArray ])
         (fun read -> read.text "email" |> Email.deserialize)

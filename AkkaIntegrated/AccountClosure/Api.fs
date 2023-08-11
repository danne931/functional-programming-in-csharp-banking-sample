module Bank.AccountClosure.Api

open System
open System.Threading.Tasks

open Lib.Postgres

// These records are held onto for reporting and legal reasons
// for 3 months following an account closure.
// The account closure actor schedules this deletion.
//
// ON CASCADE DELETE is configured. Deletion of a user
// with a given account ID deletes corresponding billing
// statements in the same transaction.
let deleteHistoricalRecords (accountIds: Guid list) : string list option Task =
   if accountIds.IsEmpty then
      Task.FromResult None
   else
      pgQuery<string>
         "DELETE FROM users WHERE account_id = ANY(@accountIds) RETURNING email"
         (Some [ "@accountIds", accountIds |> List.toArray |> Sql.uuidArray ])
         (fun read -> read.text "email")

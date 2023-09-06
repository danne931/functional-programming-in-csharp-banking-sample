module Lib.Postgres

open System.Threading.Tasks
open Npgsql.FSharp
open FsToolkit.ErrorHandling

open Lib.Types

let private connString = EnvironmentConfig.config.ConnectionStrings.Postgres

type SqlParameter = string * SqlValue
type SqlParameterList = SqlParameter list
type SqlTransactionStatement = string * SqlParameterList list

let pgQuery<'t>
   (query: string)
   (parameters: SqlParameterList option)
   (mapper: RowReader -> 't)
   : Result<'t list option, Err> Task
   =
   connString
   |> Sql.connect
   |> Sql.query query
   |> Sql.parameters (Option.defaultValue [] parameters)
   |> Sql.executeAsync mapper
   |> Task.map (fun res -> if res.IsEmpty then None else Some res)
   |> TaskResult.ofTask
   |> TaskResult.catch DatabaseError

let pgPersist
   (query: string)
   (parameters: SqlParameterList)
   : Result<int, Err> Task
   =
   connString
   |> Sql.connect
   |> Sql.query query
   |> Sql.parameters parameters
   |> Sql.executeNonQueryAsync
   |> TaskResult.ofTask
   |> TaskResult.catch DatabaseError

let pgTransaction
   (txn: SqlTransactionStatement list)
   : Result<int list, Err> Task
   =
   connString
   |> Sql.connect
   |> Sql.executeTransactionAsync txn
   |> TaskResult.ofTask
   |> TaskResult.catch DatabaseError

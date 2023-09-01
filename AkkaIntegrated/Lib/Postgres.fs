module Lib.Postgres

open System
open System.Threading.Tasks
open Npgsql.FSharp

let private connString =
   Environment.GetEnvironmentVariable "PostgresConnectionString"

type SqlParameter = string * SqlValue
type SqlParameterList = SqlParameter list
type SqlTransactionStatement = string * SqlParameterList list

let pgQuery<'t>
   (query: string)
   (parameters: (string * SqlValue) list option)
   (mapper: RowReader -> 't)
   : 't list option Task
   =
   task {
      let! res =
         connString
         |> Sql.connect
         |> Sql.query query
         |> Sql.parameters (Option.defaultValue [] parameters)
         |> Sql.executeAsync mapper

      return if res.IsEmpty then None else Some res
   }

let pgPersist (query: string) (parameters: SqlParameterList) : int Task =
   connString
   |> Sql.connect
   |> Sql.query query
   |> Sql.parameters parameters
   |> Sql.executeNonQueryAsync

let pgTransaction
   (txn: SqlTransactionStatement list)
   : Result<int list, string> Task
   =
   task {
      try
         let! res = connString |> Sql.connect |> Sql.executeTransactionAsync txn
         return Ok res
      with e ->
         return Error e.Message
   }

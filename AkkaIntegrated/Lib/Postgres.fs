module Lib.Postgres

open System
open System.Threading.Tasks
open Npgsql.FSharp

let private connString =
   Environment.GetEnvironmentVariable "PostgresConnectionString"

let pgGet<'t>
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

let pgPersist
   (query: string)
   (parameters: (string * SqlValue) list)
   : int Task
   =
   connString
   |> Sql.connect
   |> Sql.query query
   |> Sql.parameters parameters
   |> Sql.executeNonQueryAsync

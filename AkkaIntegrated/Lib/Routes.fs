[<RequireQualifiedAccessAttribute>]
module RouteUtil

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open type Microsoft.AspNetCore.Http.Results

open Lib.Types

let private json (res: 't) : IResult = Json(res, Serialization.jsonOptions)

let private badRequest (err: Err) : IResult =
   BadRequest <| {| validationError = string err |}

let private unwrapOption (opt: 't option) : IResult =
   match opt with
   | None -> NotFound()
   | Some res -> json res

let unwrapTask (future: 'a Task) : Task<IResult> =
   match future.IsFaulted with
   | true -> Problem future.Exception.Message
   | false -> Ok future.Result
   |> Task.FromResult

let unwrapTaskOption (future: 'a option Task) : Task<IResult> =
   match future.IsFaulted with
   | true -> Problem future.Exception.Message
   | false -> unwrapOption future.Result
   |> Task.FromResult

let unwrapTaskResult (future: Result<'a, Err> Task) : Task<IResult> =
   Task.FromResult(
      match future.IsFaulted with
      | true -> Problem future.Exception.Message
      | false ->
         match future.Result with
         | Error e -> badRequest e
         | Ok res -> json res
   )

let unwrapTaskResultOption
   (future: Result<'a option, Err> Task)
   : Task<IResult>
   =
   Task.FromResult(
      match future.IsFaulted with
      | true -> Problem future.Exception.Message
      | false ->
         match future.Result with
         | Error e -> badRequest e
         | Ok res -> unwrapOption res
   )

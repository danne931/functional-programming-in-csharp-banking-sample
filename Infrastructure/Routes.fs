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

let unwrapTask (future: 'a Task) : Task<IResult> = task {
   try
      let! res = future
      return Ok res
   with e ->
      return Problem e.Message
}

let unwrapTaskOption (future: 'a option Task) : Task<IResult> = task {
   try
      let! res = future
      return unwrapOption res
   with e ->
      return Problem e.Message
}

let unwrapTaskResult (future: Result<'a, Err> Task) : Task<IResult> = task {
   try
      let! res = future

      return
         match res with
         | Error e -> badRequest e
         | Ok res -> json res
   with e ->
      return Problem e.Message
}

let unwrapTaskResultOption
   (future: Result<'a option, Err> Task)
   : Task<IResult>
   =
   task {
      try
         let! res = future

         return
            match res with
            | Error e -> badRequest e
            | Ok res -> unwrapOption res
      with e ->
         return Problem e.Message
   }

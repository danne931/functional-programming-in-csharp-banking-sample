[<RequireQualifiedAccessAttribute>]
module RouteUtil

open System.Threading.Tasks
open type Microsoft.AspNetCore.Http.Results

let unwrapTask (task: 'a Task) =
   Task.FromResult(
      if task.IsFaulted then
         Problem task.Exception.Message
      else
         Ok task.Result
   )

let unwrapTaskOption (task: 'a option Task) =
   Task.FromResult(
      if task.IsFaulted then
         Problem task.Exception.Message
      else
         match task.Result with
         | None -> NotFound()
         | Some(res) -> Json(res, Serialization.jsonOptions)
   )

let unwrapTaskValidation (task: Result<'a, string> Task) =
   Task.FromResult(
      if task.IsFaulted then
         Problem task.Exception.Message
      else
         match task.Result with
         | Error e -> BadRequest {| validationError = e |}
         | Ok res -> Json(res, Serialization.jsonOptions)
   )

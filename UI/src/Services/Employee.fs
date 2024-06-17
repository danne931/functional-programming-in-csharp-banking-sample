[<RequireQualifiedAccess>]
module EmployeeService

open Fable.SimpleHttp
open FsToolkit.ErrorHandling
open Feliz.Router

open Bank.Employee.Domain
open Bank.Employee.UIDomain
open Lib.SharedTypes
open RoutePaths

let serviceName = "EmployeeService"

let private notImplemented (cmd: EmployeeCommand) =
   let msg = $"{serviceName}: Not implemented command: {cmd}"
   Log.error msg
   failwith msg

let postJson (command: EmployeeCommand) =
   let serialized, url =
      match command with
      | EmployeeCommand.DebitRequest cmd ->
         Serialization.serialize cmd, EmployeePath.Debit
      | EmployeeCommand.LimitDailyDebits cmd ->
         Serialization.serialize cmd, EmployeePath.DailyDebitLimit
      | EmployeeCommand.LockCard cmd ->
         Serialization.serialize cmd, EmployeePath.LockCard
      | EmployeeCommand.UnlockCard cmd ->
         Serialization.serialize cmd, EmployeePath.UnlockCard
      | other -> notImplemented other

   Http.postJson url serialized

let private getEmployeesWithOptionalSearchQuery
   (orgId: OrgId)
   (searchQuery: string option)
   : Async<EmployeesMaybe>
   =
   async {
      let path = EmployeePath.get orgId

      let path =
         match searchQuery with
         | None -> path
         | Some query ->
            path + Router.encodeQueryString [ ("searchQuery", query) ]

      let! (code, responseText) = Http.get path

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         return
            responseText
            |> Serialization.deserialize<Employee list>
            |> Result.map Some
   }

let getEmployees (orgId: OrgId) : Async<EmployeesMaybe> =
   getEmployeesWithOptionalSearchQuery orgId None

let searchEmployees
   (orgId: OrgId)
   (searchQuery: string)
   : Async<EmployeesMaybe>
   =
   getEmployeesWithOptionalSearchQuery orgId (Some searchQuery)

let submitCommand
   (employee: Employee)
   (command: EmployeeCommand)
   : Async<Result<CommandProcessingResponse, Err>>
   =
   asyncResult {
      // Pre-network request validation checks the command itself
      // (Err.ValidationError) and the Result of applying the command
      // to the current state (Err.EmployeeStateTransitionError).
      // This same validation occurs on the server when an actor is
      // processing a command.
      let! _ = Employee.stateTransition employee command

      let! res = postJson command
      let code = res.statusCode

      if code <> 200 then
         return! Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         let! deserialized =
            Serialization.deserialize<Result<CommandProcessingResponse, Err>>
               res.responseText

         return! deserialized
   }

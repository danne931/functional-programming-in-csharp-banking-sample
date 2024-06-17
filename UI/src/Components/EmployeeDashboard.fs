module EmployeeDashboard

open Feliz
open Feliz.UseElmish
open Elmish
open Feliz.Router

open AsyncUtil
open Bank.Employee.Domain
open Bank.Employee.UIDomain
open Lib.SharedTypes

type State = { Employees: Deferred<EmployeesMaybe> }

type Msg = LoadEmployees of AsyncOperationStatus<EmployeesMaybe>

let init () =
   { Employees = Deferred.Idle }, Cmd.ofMsg (LoadEmployees Started)

let update msg state =
   match msg with
   | LoadEmployees Started ->
      let loadEmployees = async {
         // TODO: Get OrgId from user session.
         let! res = EmployeeService.getEmployees ORG_ID_REMOVE_SOON
         return LoadEmployees(Finished res)
      }

      {
         state with
            Employees = Deferred.InProgress
      },
      Cmd.fromAsync loadEmployees
   | LoadEmployees(Finished(Ok(Some employees))) ->
      {
         state with
            Employees = Deferred.Resolved(Ok(Some employees))
      },
      Cmd.none
   | LoadEmployees(Finished(Ok None)) ->
      {
         state with
            Employees = Deferred.Resolved(Ok None)
      },
      Cmd.none
   | LoadEmployees(Finished(Error err)) ->
      {
         state with
            Employees = Deferred.Resolved(Error err)
      },
      Cmd.none

[<ReactComponent>]
let EmployeeDashboardComponent (url: Routes.EmployeeUrl) =
   let state, dispatch = React.useElmish (init, update, [||])

   classyNode Html.div [ "employee-dashboard" ] [
      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [
               Html.h5 "Employees"

            ]

            Html.aside []
         ]
      ]
   ]

module EmployeeDetail

open Feliz
open Feliz.UseElmish
open Elmish
open Elmish.SweetAlert

open Bank.Employee.Domain
open UIDomain.Employee
open Dropdown
open Bank.Employee.Forms.EmployeeRoleForm
open Lib.SharedTypes

type State = {
   PendingRole: Role option
   IsEditingRole: bool
}

type Msg =
   | OpenRoleEdit
   | CancelRoleEdit
   | SetPendingRole of Role option
   | ConfirmRole of EmployeeCommandReceipt
   | ResendInviteNotification of
      Employee *
      AsyncOperationStatus<Result<unit, Err>>
   | ShowInviteCancellationConfirmation of Employee
   | ConfirmInviteCancellation of
      Employee *
      AsyncOperationStatus<Result<EmployeeCommandReceipt, Err>>
   | RestoreAccess of
      Employee *
      AsyncOperationStatus<Result<EmployeeCommandReceipt, Err>>
   | DismissInviteCancellation

let init () =
   {
      PendingRole = None
      IsEditingRole = false
   },
   Cmd.none

let closeRoleSelect state = {
   state with
      IsEditingRole = false
      PendingRole = None
}

let update
   (notifyParentOnUpdate: EmployeeCommandReceipt -> unit)
   (session: UserSession)
   msg
   state
   =
   match msg with
   | OpenRoleEdit -> { state with IsEditingRole = true }, Cmd.none
   | CancelRoleEdit -> closeRoleSelect state, Cmd.none
   | SetPendingRole role -> { state with PendingRole = role }, Cmd.none
   | ConfirmRole receipt ->
      notifyParentOnUpdate receipt
      closeRoleSelect state, Cmd.none
   | ResendInviteNotification(employee, Started) ->
      let send = async {
         let! res = EmployeeService.resendEmployeeInvitation employee
         return Msg.ResendInviteNotification(employee, Finished res)
      }

      state, Cmd.fromAsync send
   | ResendInviteNotification(employee, Finished(Ok _)) ->
      state, Alerts.toastSuccessCommand $"Invite resent to {employee.Name}"
   | ResendInviteNotification(employee, Finished(Error _)) ->
      state,
      Alerts.toastCommand
      <| Err.NetworkError(exn $"Issue resending invite to {employee.Email}")
   | ShowInviteCancellationConfirmation employee ->
      let confirm =
         ConfirmAlert(
            "",
            function
            | ConfirmAlertResult.Confirmed ->
               Msg.ConfirmInviteCancellation(employee, Started)
            | ConfirmAlertResult.Dismissed _ -> Msg.DismissInviteCancellation
         )
            .Title("Are you sure you want to cancel this invitation?")
            .Type(AlertType.Question)
            .ShowCloseButton(true)

      state, SweetAlert.Run confirm
   | ConfirmInviteCancellation(employee, Started) ->
      let cancel = async {
         let cmd =
            CancelInvitationCommand.create
               employee.CompositeId
               (InitiatedById session.EmployeeId)
               { Reason = None }
            |> EmployeeCommand.CancelInvitation

         let! res = EmployeeService.submitCommand employee cmd
         return ConfirmInviteCancellation(employee, Finished res)
      }

      state, Cmd.fromAsync cancel
   | ConfirmInviteCancellation(employee, Finished(Ok receipt)) ->
      notifyParentOnUpdate receipt
      state, Alerts.toastSuccessCommand $"Cancelling invite for {employee.Name}"
   | ConfirmInviteCancellation(employee, Finished(Error err)) ->
      Log.error (string err)

      state,
      Alerts.toastCommand
      <| Err.NetworkError(exn $"Issue cancelling invite for {employee.Name}")
   | DismissInviteCancellation -> state, Cmd.none
   | RestoreAccess(employee, Started) ->
      let send = async {
         let cmd =
            RestoreAccessCommand.create
               employee.CompositeId
               (InitiatedById session.EmployeeId)
               { Reference = None }
            |> EmployeeCommand.RestoreAccess

         let! res = EmployeeService.submitCommand employee cmd
         return Msg.RestoreAccess(employee, Finished res)
      }

      state, Cmd.fromAsync send
   | RestoreAccess(employee, Finished(Ok receipt)) ->
      notifyParentOnUpdate receipt
      state, Alerts.toastSuccessCommand $"Access restored for {employee.Name}"
   | RestoreAccess(_, Finished(Error err)) -> state, Alerts.toastCommand err

[<ReactComponent>]
let EmployeeDetailComponent
   (session: UserSession)
   (employee: Employee)
   (notifyParentOnUpdate: EmployeeCommandReceipt -> unit)
   =
   let state, dispatch =
      React.useElmish (init, update notifyParentOnUpdate session, [||])

   React.fragment [
      classyNode Html.div [ "employee-detail-top-section" ] [
         Html.div [
            Html.p employee.Name
            Html.p [
               attr.role "employee-role"
               attr.text employee.Role.Display
            ]
         ]

         Html.div [ Html.small (string employee.Email) ]

         DropdownComponent
            DropdownDirection.LTR
            false
            None
            (match employee.Status with
             | EmployeeStatus.Active -> [
                {
                   Text = "Edit Role"
                   OnClick = fun _ -> dispatch Msg.OpenRoleEdit
                   IsSelected = state.IsEditingRole
                }
               ]
             | EmployeeStatus.PendingInviteApproval -> [
                {
                   Text = "Remove Invite"
                   OnClick =
                      fun _ ->
                         dispatch
                         <| Msg.ShowInviteCancellationConfirmation employee
                   IsSelected = false
                }
               ]
             | EmployeeStatus.PendingInviteConfirmation _ -> [
                {
                   Text = "Remove Invite"
                   OnClick =
                      fun _ ->
                         dispatch
                         <| Msg.ShowInviteCancellationConfirmation employee
                   IsSelected = false
                }
                {
                   Text = "Resend Invite"
                   OnClick =
                      fun _ ->
                         dispatch
                         <| Msg.ResendInviteNotification(employee, Started)
                   IsSelected = false
                }
               ]
             | EmployeeStatus.Closed -> [
                {
                   IsSelected = false
                   Text = "Restore Access"
                   OnClick =
                      fun _ -> dispatch <| Msg.RestoreAccess(employee, Started)
                }
               ]
             | _ -> [])
      ]

      Html.hr []

      if state.IsEditingRole then
         EmployeeRoleFormComponent
            (fun () -> Msg.CancelRoleEdit |> dispatch)
            (Some >> Msg.SetPendingRole >> dispatch)
            (Msg.ConfirmRole >> dispatch)
            employee

      state.PendingRole
      |> Option.defaultValue employee.Role
      |> EmployeePermissions.render
   ]

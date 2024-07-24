module Bank.Employee.Forms.EmployeeRoleForm

open Feliz
open Fable.Form.Simple

open Fable.Form.Simple.Pico
open Bank.Employee.Domain
open Bank.Account.Domain
open FormContainer
open Lib.SharedTypes
open DailyPurchaseLimitForm
open MonthlyPurchaseLimitForm
open AccountProfileForm

type Values = {
   Role: string
   DailyPurchaseLimit: string
   MonthlyPurchaseLimit: string
   LinkedAccountId: string
}

let private options = [
   string Role.Admin, Role.Admin.Display
   string Role.CardOnly, Role.CardOnly.Display
   string Role.Scholar, Role.Scholar.Display
]

let employeeRoleSelect (onSelect: Role -> unit) =
   Form.selectField {
      Parser = Role.fromStringUnsafe >> Ok
      Value = fun values -> values.Role
      Update =
         fun newValue values ->
            onSelect (Role.fromStringUnsafe newValue)
            { values with Role = newValue }
      Error = fun _ -> None
      Attributes = {
         Label = "Select a role:"
         Placeholder = ""
         Options = options
      }
   }

let private form
   (initiatedBy: UserSession)
   (employee: Employee)
   (accountProfiles: Map<AccountId, AccountProfile>)
   (onRoleSelect: Role -> unit)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let onSubmit
      (role: Role)
      (cardInfo: EmployeeInviteSupplementaryCardInfo option)
      =
      let cmd =
         UpdateRoleCommand.create
            employee.CompositeId
            (InitiatedById initiatedBy.EmployeeId)
            {
               PriorRole = employee.Role
               Role = role
               CardInfo = cardInfo
            }
         |> EmployeeCommand.UpdateRole

      Msg.Submit(employee, cmd, Started)

   employeeRoleSelect onRoleSelect
   |> Form.andThen (fun role ->
      match role with
      | Role.CardOnly when not employee.HasCard ->
         Form.succeed
            (fun accountId dailyPurchaseLimit monthlyPurchaseLimit ->
               let cardInfo =
                  Some {
                     LinkedAccountId = accountId
                     DailyPurchaseLimit = dailyPurchaseLimit
                     MonthlyPurchaseLimit = monthlyPurchaseLimit
                  }

               onSubmit role cardInfo)
         |> Form.append (
            accountProfileSelect accountProfiles
            |> Form.mapValues {
               Value = fun a -> { LinkedAccountId = a.LinkedAccountId }
               Update =
                  fun a b -> {
                     b with
                        LinkedAccountId = a.LinkedAccountId
                  }
            }
         )
         |> Form.append (
            dailyPurchaseLimitField
            |> Form.mapValues {
               Value = fun a -> { Amount = a.DailyPurchaseLimit }
               Update = fun a b -> { b with DailyPurchaseLimit = a.Amount }
            }
         )
         |> Form.append (
            monthlyPurchaseLimitField
            |> Form.mapValues {
               Value = fun a -> { Amount = a.MonthlyPurchaseLimit }
               Update =
                  fun a b -> {
                     b with
                        MonthlyPurchaseLimit = a.Amount
                  }
            }
         )
      | _ -> Form.succeed (onSubmit role None))

[<ReactComponent>]
let EmployeeRoleFormComponent
   (onCancel: unit -> unit)
   (onSelect: Role -> unit)
   (onSubmit: ParentOnSubmitHandler)
   (employee: Employee)
   =
   let session = React.useContext UserSessionProvider.context
   let orgCtx = React.useContext OrgAndAccountProfileProvider.context
   let selectedRole, setSelectedRole = React.useState employee.Role

   let onSelect (role: Role) =
      setSelectedRole role
      onSelect role

   let formProps: Values = {
      Role = string employee.Role
      DailyPurchaseLimit = string Card.DAILY_PURCHASE_LIMIT_DEFAULT
      MonthlyPurchaseLimit = string Card.MONTHLY_PURCHASE_LIMIT_DEFAULT
      LinkedAccountId = ""
   }

   match orgCtx.AccountProfiles, session with
   | Deferred.Resolved(Ok(Some profiles)), Deferred.Resolved session ->
      EmployeeFormContainer
      <| formProps
      <| form session employee profiles onSelect
      <| onSubmit
      <| Some(
         Form.View.Action.Custom(fun state _ ->
            if selectedRole = employee.Role then
               Form.View.cancelButton onCancel state
            else
               Form.View.submitAndCancelButton "Save Role" onCancel state)
      )
   | _ -> Html.progress []

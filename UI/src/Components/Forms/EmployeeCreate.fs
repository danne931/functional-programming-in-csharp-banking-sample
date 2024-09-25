module Bank.Employee.Forms.EmployeeCreateForm

open Feliz
open Fable.Form.Simple

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Employee.Domain
open Lib.Validators
open FormContainer
open EmployeeRoleForm
open DailyPurchaseLimitForm
open MonthlyPurchaseLimitForm
open AccountProfileForm
open Lib.SharedTypes

type Values = {
   FirstName: string
   LastName: string
   Email: string
   Role: string
   DailyPurchaseLimit: string
   MonthlyPurchaseLimit: string
   LinkedAccountId: string
}

let form
   (initiatedBy: UserSession)
   (org: Org)
   (accounts: Map<AccountId, Account>)
   (onRoleSelect: Role -> unit)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let fieldFirstName =
      Form.textField {
         Parser = firstNameValidator >> validationErrorsHumanFriendly
         Value = fun (values: Values) -> values.FirstName
         Update = fun newValue values -> { values with FirstName = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "First Name:"
            Placeholder = "First Name"
            HtmlAttributes = []
         }
      }

   let fieldLastName =
      Form.textField {
         Parser = lastNameValidator >> validationErrorsHumanFriendly
         Value = fun (values: Values) -> values.LastName
         Update = fun newValue values -> { values with LastName = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Last Name:"
            Placeholder = "Last Name"
            HtmlAttributes = []
         }
      }

   let fieldEmail =
      Form.textField {
         Parser =
            Email.ofString "Email"
            >> Result.map string
            >> validationErrorsHumanFriendly
         Value = fun (values: Values) -> values.Email
         Update = fun newValue values -> { values with Email = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Email:"
            Placeholder = "Email Address"
            HtmlAttributes = [ attr.autoComplete "email" ]
         }
      }

   let onSubmit
      (role: Role)
      (cardInfo: EmployeeInviteSupplementaryCardInfo option)
      firstName
      lastName
      (email: string)
      =
      let cmd =
         CreateEmployeeCommand.create (InitiatedById initiatedBy.EmployeeId) {
            Email = email
            FirstName = firstName
            LastName = lastName
            Role = role
            OrgId = initiatedBy.OrgId
            OrgRequiresEmployeeInviteApproval =
               org.Permissions.RequiresEmployeeInviteApproval
            CardInfo = cardInfo
         }
         |> EmployeeCommand.CreateEmployee

      Msg.Submit(Employee.empty, cmd, Started)

   let roleField = employeeRoleSelect onRoleSelect

   roleField
   |> Form.mapValues {
      Value =
         fun a -> {
            Role = a.Role
            DailyPurchaseLimit = ""
            MonthlyPurchaseLimit = ""
            LinkedAccountId = ""
         }
      Update = fun a b -> { b with Role = a.Role }
   }
   |> Form.andThen (fun role ->
      match role with
      | Role.CardOnly ->
         Form.succeed
            (fun
                 fName
                 lName
                 email
                 accountId
                 dailyPurchaseLimit
                 monthlyPurchaseLimit ->
               let cardInfo =
                  Some {
                     LinkedAccountId = accountId
                     DailyPurchaseLimit = dailyPurchaseLimit
                     MonthlyPurchaseLimit = monthlyPurchaseLimit
                  }

               onSubmit role cardInfo fName lName email)
         |> Form.append fieldFirstName
         |> Form.append fieldLastName
         |> Form.append fieldEmail
         |> Form.append (
            accountProfileSelect accounts
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
      | _ ->
         Form.succeed (onSubmit role None)
         |> Form.append fieldFirstName
         |> Form.append fieldLastName
         |> Form.append fieldEmail)

[<ReactComponent>]
let EmployeeCreateFormComponent
   (session: UserSession)
   (onSubmit: ParentOnSubmitHandler)
   =
   let orgCtx = React.useContext OrgProvider.context
   let pendingRole, setRole = React.useState Role.CardOnly

   let formProps: Values = {
      FirstName = ""
      LastName = ""
      Email = ""
      Role = string Role.CardOnly
      LinkedAccountId = ""
      DailyPurchaseLimit = string Constants.DAILY_PURCHASE_LIMIT_DEFAULT
      MonthlyPurchaseLimit = string Constants.MONTHLY_PURCHASE_LIMIT_DEFAULT
   }

   classyNode Html.div [ "grid" ] [
      EmployeePermissions.render pendingRole

      match orgCtx with
      | Deferred.Resolved(Ok(Some org)) ->
         let submitText =
            if org.Org.Permissions.RequiresEmployeeInviteApproval then
               "Request Employee Invite Approval"
            else
               "Invite Employee"

         EmployeeFormContainer
            formProps
            (form session org.Org org.Accounts setRole)
            onSubmit
            (Some <| Form.View.Action.SubmitOnly submitText)
      | _ -> Html.progress []
   ]

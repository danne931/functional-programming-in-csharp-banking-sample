module Bank.Account.Forms.RegisterTransferRecipientForm

open Feliz
open Fable.Form.Simple

open Fable.Form.Simple.Pico
open Bank.Account.UIDomain
open Bank.Account.Domain
open Bank.Transfer.Domain
open AsyncUtil
open Lib.Validators
open FormContainer

type Values = {
   AccountEnvironment: string
   FirstName: string
   LastName: string
   Email: string
   AccountNumber: string
   RoutingNumber: string
}

type State = {
   Account: Account
   PotentialInternalRecipients: PotentialInternalTransferRecipients
}

let internalRecipientForm
   (account: Account)
   (potentialInternalRecipients: PotentialInternalTransferRecipients)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let fieldEmail =
      Form.emailField {
         Parser =
            fun email ->
               let accountOpt =
                  potentialInternalRecipients
                  |> PotentialInternalTransferRecipients.value
                  |> Map.tryPick (fun id account ->
                     if (string account.Email) = email then
                        Some account
                     else
                        None)

               match accountOpt with
               | None -> Error "No account found with that email"
               | Some account -> Ok account
         Value = fun (values: Values) -> values.Email
         Update = fun newValue values -> { values with Email = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Email:"
            Placeholder = "Email"
            HtmlAttributes = []
         }
      }

   let onSubmit (recipient: AccountProfile) =
      let cmd =
         RegisterInternalTransferRecipientCommand.create account.CompositeId {
            AccountId = recipient.AccountId
            FirstName = recipient.FirstName
            LastName = recipient.LastName
         }

      Msg.Submit(AccountCommand.RegisterInternalTransferRecipient cmd, Started)

   Form.succeed onSubmit |> Form.append fieldEmail

let domesticRecipientForm
   (account: Account)
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

   let fieldAccountNumber =
      Form.textField {
         Parser = accountNumberValidator >> validationErrorsHumanFriendly
         Value = fun (values: Values) -> values.AccountNumber
         Update =
            fun newValue values -> { values with AccountNumber = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Account Number:"
            Placeholder = "Account Number"
            HtmlAttributes = []
         }
      }

   let fieldRoutingNumber =
      Form.textField {
         Parser = routingNumberValidator >> validationErrorsHumanFriendly
         Value = fun (values: Values) -> values.RoutingNumber
         Update =
            fun newValue values -> { values with RoutingNumber = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Routing Number:"
            Placeholder = "Routing Number"
            HtmlAttributes = []
         }
      }

   let onSubmit name accountNum routingNum =
      let first, last = name

      let cmd =
         RegisterDomesticTransferRecipientCommand.create account.CompositeId {
            LastName = last
            FirstName = first
            AccountNumber = accountNum
            RoutingNumber = routingNum
         }

      Msg.Submit(AccountCommand.RegisterDomesticTransferRecipient cmd, Started)

   Form.succeed onSubmit
   |> Form.append (
      Form.succeed (fun first last -> first, last)
      |> Form.append fieldFirstName
      |> Form.append fieldLastName
      |> Form.group
   )
   |> Form.append fieldAccountNumber
   |> Form.append fieldRoutingNumber

let form (state: State) : Form.Form<Values, Msg<Values>, IReactProperty> =
   let fieldAccountEnvironment =
      Form.selectField {
         Parser =
            fun envId ->
               let env =
                  match envId with
                  | "internal" -> RecipientAccountEnvironment.Internal
                  | "domestic" -> RecipientAccountEnvironment.Domestic
                  | other ->
                     failwith $"Not implemented account environment {other}"

               Ok env
         Value = fun (values: Values) -> values.AccountEnvironment
         Update =
            fun newValue values -> {
               values with
                  AccountEnvironment = newValue
            }
         Error = fun _ -> None
         Attributes = {
            Label = "Does this account belong to our bank?"
            Placeholder = ""
            Options = [ "internal", "Our bank"; "domestic", "Domestic bank" ]
         }
      }

   fieldAccountEnvironment
   |> Form.andThen (fun env ->
      match env with
      | RecipientAccountEnvironment.Internal ->
         internalRecipientForm state.Account state.PotentialInternalRecipients
      | RecipientAccountEnvironment.Domestic ->
         domesticRecipientForm state.Account)

[<ReactComponent>]
let RegisterTransferRecipientFormComponent
   (account: Account)
   (potentialTransferRecipients: PotentialInternalTransferRecipients)
   (onSubmit: ParentOnSubmitHandler)
   =
   // Capture state
   let state, _ =
      React.useState {
         Account = account
         PotentialInternalRecipients = potentialTransferRecipients
      }

   FormContainer
      state.Account
      {
         AccountEnvironment = "internal"
         FirstName = ""
         LastName = ""
         Email = ""
         AccountNumber = ""
         RoutingNumber = ""
      }
      (form state)
      onSubmit

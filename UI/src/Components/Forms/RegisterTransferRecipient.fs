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
open Lib.SharedTypes

type Values = {
   AccountEnvironment: string
   FirstName: string
   LastName: string
   Email: string
   AccountNumber: string
   RoutingNumber: string
   AccountDepository: string
   PaymentNetwork: string
}

type State = {
   Account: Account
   PotentialInternalRecipients: PotentialInternalTransferRecipients
   EditingDomesticRecipient: DomesticTransferRecipient option
}

let internalRecipientForm
   (state: State)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let fieldEmail =
      Form.emailField {
         Parser =
            fun email ->
               let accountOpt =
                  state.PotentialInternalRecipients
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
         RegisterInternalTransferRecipientCommand.create
            state.Account.CompositeId
            {
               AccountId = recipient.AccountId
               FirstName = recipient.FirstName
               LastName = recipient.LastName
            }

      Msg.Submit(AccountCommand.RegisterInternalTransferRecipient cmd, Started)

   Form.succeed onSubmit |> Form.append fieldEmail

let domesticRecipientForm
   (state: State)
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
         Parser =
            accountNumberValidator "Account Number"
            >> validationErrorsHumanFriendly
            >> Result.map string
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
         Parser =
            routingNumberValidator "Routing Number"
            >> validationErrorsHumanFriendly
            >> Result.map string
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

   let fieldAccountDepository =
      Form.selectField {
         Parser =
            fun depository ->
               let depository =
                  match depository with
                  | "checking" -> DomesticRecipientAccountDepository.Checking
                  | "savings" -> DomesticRecipientAccountDepository.Savings
                  | other ->
                     failwith $"Not implemented account depository {other}"

               Ok depository
         Value = fun (values: Values) -> values.AccountDepository
         Update =
            fun newValue values -> {
               values with
                  AccountDepository = newValue
            }
         Error = fun _ -> None
         Attributes = {
            Label = "Account Type"
            Placeholder = ""
            Options = [ "checking", "Checking"; "savings", "Savings" ]
         }
      }

   let fieldPaymentNetwork =
      Form.selectField {
         Parser =
            fun pay ->
               let pay =
                  match pay with
                  | "ach" -> PaymentNetwork.ACH
                  | other -> failwith $"Not implemented payment network {other}"

               Ok pay
         Value = fun (values: Values) -> values.PaymentNetwork
         Update =
            fun newValue values -> {
               values with
                  PaymentNetwork = newValue
            }
         Error = fun _ -> None
         Attributes = {
            Label = "Payment Network"
            Placeholder = ""
            Options = [ "ach", "ACH" ]
         }
      }

   let onSubmit paymentNetwork depository name accountNum routingNum =
      let first, last = name

      let cmd =
         match state.EditingDomesticRecipient with
         | None ->
            RegisterDomesticTransferRecipientCommand.create
               state.Account.CompositeId
               {
                  LastName = last
                  FirstName = first
                  AccountNumber = accountNum
                  RoutingNumber = routingNum
                  Depository = depository
                  PaymentNetwork = paymentNetwork
               }
            |> AccountCommand.RegisterDomesticTransferRecipient
         | Some recipient ->
            EditDomesticTransferRecipientCommand.create
               state.Account.CompositeId
               {
                  LastName = last
                  FirstName = first
                  AccountNumber = accountNum
                  RoutingNumber = routingNum
                  Depository = depository
                  PaymentNetwork = paymentNetwork
                  RecipientWithoutAppliedUpdates = recipient
               }
            |> AccountCommand.EditDomesticTransferRecipient

      Msg.Submit(cmd, Started)

   Form.succeed onSubmit
   |> Form.append fieldPaymentNetwork
   |> Form.append fieldAccountDepository
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
      | RecipientAccountEnvironment.Internal -> internalRecipientForm state
      | RecipientAccountEnvironment.Domestic -> domesticRecipientForm state)

[<ReactComponent>]
let RegisterTransferRecipientFormComponent
   (account: Account)
   (potentialTransferRecipients: PotentialInternalTransferRecipients)
   (recipientIdForEdit: AccountId option)
   (onSubmit: ParentOnSubmitHandler)
   =
   // Capture state
   let state, _ =
      React.useState {
         Account = account
         PotentialInternalRecipients = potentialTransferRecipients
         EditingDomesticRecipient =
            recipientIdForEdit
            |> Option.bind (fun accountId ->
               Map.tryFind accountId account.DomesticTransferRecipients)
      }

   let formProps = {
      AccountEnvironment = "internal"
      AccountDepository = "checking"
      PaymentNetwork = "ach"
      FirstName = ""
      LastName = ""
      Email = ""
      AccountNumber = ""
      RoutingNumber = ""
   }

   let formProps =
      match state.EditingDomesticRecipient with
      | Some recipient -> {
         formProps with
            AccountEnvironment = "domestic"
            FirstName = recipient.FirstName
            LastName = recipient.LastName
            AccountNumber = string recipient.AccountNumber
            RoutingNumber = string recipient.RoutingNumber
        }
      | _ -> formProps

   FormContainer state.Account formProps (form state) onSubmit

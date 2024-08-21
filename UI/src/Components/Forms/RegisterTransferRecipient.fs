module Bank.Account.Forms.RegisterTransferRecipientForm

open Feliz
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.Employee.Domain
open Lib.Validators
open FormContainer
open Lib.SharedTypes

type Values = {
   AccountEnvironment: string
   FirstName: string
   LastName: string
   SelectedInternalAccountId: string
   AccountNumber: string
   RoutingNumber: string
   AccountDepository: string
   PaymentNetwork: string
}

type State = {
   Account: Account
   EditingDomesticRecipient: DomesticTransferRecipient option
}

let domesticRecipientForm
   (state: State)
   (initiatedBy: InitiatedById)
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
            AccountNumber.fromString "Account Number"
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
            RoutingNumber.fromString "Routing Number"
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
               initiatedBy
               {
                  AccountId = AccountId <| Guid.NewGuid()
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
               initiatedBy
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

      Msg.Submit(state.Account, cmd, Started)

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

let form
   (state: State)
   (initiatedBy: InitiatedById)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let fieldAccountEnvironment =
      Form.selectField {
         Parser =
            fun envId ->
               let env =
                  match envId with
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
            Label = ""
            Placeholder = ""
            Options = [ "domestic", "Domestic Account" ]
         }
      }

   fieldAccountEnvironment
   |> Form.andThen (fun _ -> domesticRecipientForm state initiatedBy)

let RegisterTransferRecipientFormComponent
   (session: UserSession)
   (account: Account)
   (recipientIdForEdit: AccountId option)
   (onSubmit: ParentOnSubmitHandler)
   =
   let recipient =
      recipientIdForEdit
      |> Option.bind (fun accountId ->
         Map.tryFind accountId account.DomesticTransferRecipients)

   let formProps: Values = {
      AccountEnvironment = "domestic"
      AccountDepository = "checking"
      PaymentNetwork = "ach"
      FirstName = ""
      LastName = ""
      SelectedInternalAccountId = ""
      AccountNumber = ""
      RoutingNumber = ""
   }

   let formProps =
      match recipient with
      | Some recipient -> {
         formProps with
            FirstName = recipient.FirstName
            LastName = recipient.LastName
            AccountNumber = string recipient.AccountNumber
            RoutingNumber = string recipient.RoutingNumber
        }
      | _ -> formProps

   AccountFormContainer
      formProps
      (form
         {
            Account = account
            EditingDomesticRecipient = recipient
         }
         (InitiatedById session.EmployeeId))
      onSubmit

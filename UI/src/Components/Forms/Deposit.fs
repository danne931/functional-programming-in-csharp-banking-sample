module Bank.Account.Forms.DepositForm

open Feliz
open Fable.Form.Simple

open Lib.Validators
open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Employee.Domain
open FormContainer

type Values = { Amount: string }

let form
   (account: Account)
   (initiatedBy: InitiatedById)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let amountField =
      Form.textField {
         Parser =
            amountValidatorFromString "Deposit amount"
            >> validationErrorsHumanFriendly
         Value = fun values -> values.Amount
         Update = fun newValue values -> { values with Amount = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Deposit Cash:"
            Placeholder = "100"
            HtmlAttributes = []
         }
      }

   let onSubmit amount =
      let command =
         DepositCashCommand.create account.CompositeId initiatedBy {
            Amount = amount
            Origin = Some "ATM"
         }
         |> AccountCommand.DepositCash

      Msg.Submit(account, command, Started)

   Form.succeed onSubmit |> Form.append amountField

let DepositFormComponent
   (session: UserSession)
   (account: Account)
   (onSubmit: ParentOnSubmitHandler)
   =
   AccountFormContainer
      { Amount = "" }
      (form account (InitiatedById session.EmployeeId))
      onSubmit

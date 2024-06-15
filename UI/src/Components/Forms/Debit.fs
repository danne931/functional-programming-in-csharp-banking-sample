module Bank.Account.Forms.DebitForm

open Feliz
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Employee.Domain
open AsyncUtil
open Lib.Validators
open Lib.SharedTypes
open FormContainer

type Values = { Amount: string; Origin: string }

let form
   (account: Account)
   (employee: Employee)
   (selectedCardId: CardId)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let amountField =
      Form.textField {
         Parser =
            amountValidatorFromString "Debit amount"
            >> validationErrorsHumanFriendly
         Value = fun (values: Values) -> values.Amount
         Update = fun newValue values -> { values with Amount = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Debit Amount:"
            Placeholder = "1"
            HtmlAttributes = []
         }
      }

   let originField =
      Form.textField {
         Parser = originValidator >> validationErrorsHumanFriendly
         Value = fun (values: Values) -> values.Origin
         Update = fun newValue values -> { values with Origin = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Origin:"
            Placeholder = "Trader Joe's"
            HtmlAttributes = []
         }
      }

   let onSubmit amount origin =
      let cmd =
         DebitRequestCommand.create employee.CompositeId {
            CardId = selectedCardId
            AccountId = account.AccountId
            Amount = amount
            Origin = origin
            Reference = None
            Date = DateTime.UtcNow
         }
         |> EmployeeCommand.DebitRequest
         |> FormCommand.Employee

      Msg.Submit(cmd, Started)

   Form.succeed onSubmit |> Form.append amountField |> Form.append originField

let DebitFormComponent
   (onSubmit: ParentOnSubmitHandler)
   (account: Account)
   (selectedCardId: CardId)
   (employee: Employee)
   =
   FormContainer
      (FormDomain.Employee employee)
      { Amount = ""; Origin = "" }
      (form account employee selectedCardId)
      onSubmit

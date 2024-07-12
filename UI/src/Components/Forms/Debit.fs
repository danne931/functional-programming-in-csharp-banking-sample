module Bank.Employee.Forms.DebitForm

open Feliz
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Employee.Domain
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
            CardNumberLast4 =
               employee.Cards[selectedCardId].SecurityInfo.CardNumber.Last4
            AccountId = account.AccountId
            Amount = amount
            Origin = origin
            Reference = None
            Date = DateTime.UtcNow
         }
         |> EmployeeCommand.DebitRequest

      Msg.Submit(employee, cmd, Started)

   Form.succeed onSubmit |> Form.append amountField |> Form.append originField

let DebitFormComponent
   (onSubmit: ParentOnSubmitHandler)
   (account: Account)
   (selectedCardId: CardId)
   (employee: Employee)
   =
   EmployeeFormContainer
      { Amount = ""; Origin = "" }
      (form account employee selectedCardId)
      onSubmit
      None

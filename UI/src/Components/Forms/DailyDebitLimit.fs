module Bank.Account.Forms.DailyDebitLimitForm

open Feliz
open Fable.Form.Simple

open Bank.Employee.Domain
open AsyncUtil
open Lib.Validators
open Lib.SharedTypes
open FormContainer

type Values = { Amount: string }

let form
   (selectedCardId: CardId)
   (employee: Employee)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let amountField =
      Form.textField {
         Parser =
            amountValidatorFromString "Debit limit"
            >> validationErrorsHumanFriendly
         Value = fun values -> values.Amount
         Update = fun newValue values -> { values with Amount = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Daily Debit Limit:"
            Placeholder = "250"
            HtmlAttributes = []
         }
      }

   let onSubmit amount =
      let cmd =
         LimitDailyDebitsCommand.create employee.CompositeId {
            CardId = selectedCardId
            DebitLimit = amount
         }
         |> EmployeeCommand.LimitDailyDebits
         |> FormCommand.Employee

      Msg.Submit(cmd, Started)

   Form.succeed onSubmit |> Form.append amountField

let DailyDebitLimitFormComponent
   (onSubmit: ParentOnSubmitHandler)
   (selectedCardId: CardId)
   (employee: Employee)
   =
   FormContainer
      (FormDomain.Employee employee)
      { Amount = "" }
      (form selectedCardId employee)
      onSubmit

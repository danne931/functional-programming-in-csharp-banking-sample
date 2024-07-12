module Bank.Employee.Forms.DailyPurchaseLimitForm

open Feliz
open Fable.Form.Simple

open Bank.Employee.Domain
open Lib.Validators
open Lib.SharedTypes
open FormContainer

type Values = { Amount: string }

let dailyPurchaseLimitField =
   Form.textField {
      Parser =
         amountValidatorFromString "Purchase limit"
         >> validationErrorsHumanFriendly
      Value = fun values -> values.Amount
      Update = fun newValue values -> { values with Amount = newValue }
      Error = fun _ -> None
      Attributes = {
         Label = "Daily Purchase Limit:"
         Placeholder = "250"
         HtmlAttributes = []
      }
   }

let private form
   (selectedCardId: CardId)
   (employee: Employee)
   (initiatedBy: InitiatedById)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =

   let onSubmit amount =
      let card = employee.Cards[selectedCardId]

      let cmd =
         LimitDailyDebitsCommand.create employee.CompositeId initiatedBy {
            CardId = selectedCardId
            CardNumberLast4 = card.SecurityInfo.CardNumber.Last4
            PriorLimit = card.DailyDebitLimit
            DebitLimit = amount
         }
         |> EmployeeCommand.LimitDailyDebits

      Msg.Submit(employee, cmd, Started)

   Form.succeed onSubmit |> Form.append dailyPurchaseLimitField

let DailyPurchaseLimitFormComponent
   (session: UserSession)
   (onSubmit: ParentOnSubmitHandler)
   (selectedCardId: CardId)
   (employee: Employee)
   =
   EmployeeFormContainer
      { Amount = "" }
      (form selectedCardId employee (InitiatedById session.EmployeeId))
      onSubmit
      None

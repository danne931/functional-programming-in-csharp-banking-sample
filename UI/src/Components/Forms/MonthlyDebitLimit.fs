module Bank.Employee.Forms.MonthlyPurchaseLimitForm

open Fable.Form.Simple
open Validus.Operators

open Bank.Employee.Domain
open Lib.Validators
open Lib.SharedTypes
open FormContainer

type Values = { Amount: string }

let monthlyPurchaseLimitField =
   Form.textField {
      Parser =
         (parseDecimal >=> monthlyPurchaseLimitValidator)
            "Monthly purchase limit"
         >> validationErrorsHumanFriendly
      Value = fun values -> values.Amount
      Update = fun newValue values -> { values with Amount = newValue }
      Error = fun _ -> None
      Attributes = {
         Label = "Monthly Purchase Limit:"
         Placeholder = Money.format Card.MONTHLY_PURCHASE_LIMIT_DEFAULT
         HtmlAttributes = []
      }
   }

let onSubmit (card: Card) (employee: Employee) initiatedBy amount =
   let cmd =
      LimitMonthlyDebitsCommand.create employee.CompositeId initiatedBy {
         CardId = card.CardId
         CardNumberLast4 = card.CardNumberLast4
         PriorLimit = card.MonthlyPurchaseLimit
         DebitLimit = amount
      }
      |> EmployeeCommand.LimitMonthlyDebits

   Msg.Submit(employee, cmd, Started)

let MonthlyPurchaseLimitFormComponent
   (session: UserSession)
   (notifyParentOnSubmit: ParentOnSubmitHandler)
   (card: Card)
   (employee: Employee)
   =
   let form =
      Form.succeed (onSubmit card employee (InitiatedById session.EmployeeId))
      |> Form.append monthlyPurchaseLimitField

   EmployeeFormContainer { Amount = "" } form notifyParentOnSubmit None

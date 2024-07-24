module Bank.Employee.Forms.CardAccess

open Feliz
open Fable.Form.Simple

open Lib.SharedTypes
open Bank.Employee.Domain
open FormContainer

type Values = { Locked: bool }

let form
   (employee: Employee)
   (card: Card)
   (initiatedBy: InitiatedById)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let isLockedField =
      Form.checkboxField {
         Parser = Ok
         Value = fun (values: Values) -> values.Locked
         Update = fun newValue values -> { values with Locked = newValue }
         Error = fun _ -> None
         Attributes = { Text = "Lock Card:" }
      }

   let onSubmit isLocked =
      let cmd =
         if isLocked then
            LockCardCommand.create employee.CompositeId initiatedBy {
               CardId = card.CardId
               CardNumberLast4 = card.CardNumberLast4
               Reference = None
            }
            |> EmployeeCommand.LockCard
         else
            UnlockCardCommand.create employee.CompositeId initiatedBy {
               CardId = card.CardId
               CardNumberLast4 = card.CardNumberLast4
               Reference = None
            }
            |> EmployeeCommand.UnlockCard

      Msg.Submit(employee, cmd, Started)

   Form.succeed onSubmit |> Form.append isLockedField

let CardAccessFormComponent
   (session: UserSession)
   (onSubmit: ParentOnSubmitHandler)
   (card: Card)
   (employee: Employee)
   =
   EmployeeFormContainer
      {
         Locked = card.Status = CardStatus.Frozen
      }
      (form employee card (InitiatedById session.EmployeeId))
      onSubmit
      None

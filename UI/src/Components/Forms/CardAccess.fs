module Bank.Employee.Forms.CardAccess

open Feliz
open Fable.Form.Simple

open Lib.SharedTypes
open Bank.Employee.Domain
open FormContainer

type Values = { Locked: bool }

let form
   (employee: Employee)
   (selectedCardId: CardId)
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
               CardId = selectedCardId
               CardNumberLast4 =
                  employee.Cards[selectedCardId].SecurityInfo.CardNumber.Last4
               Reference = None
            }
            |> EmployeeCommand.LockCard
         else
            UnlockCardCommand.create employee.CompositeId initiatedBy {
               CardId = selectedCardId
               CardNumberLast4 =
                  employee.Cards[selectedCardId].SecurityInfo.CardNumber.Last4
               Reference = None
            }
            |> EmployeeCommand.UnlockCard

      Msg.Submit(employee, cmd, Started)

   Form.succeed onSubmit |> Form.append isLockedField

let CardAccessFormComponent
   (session: UserSession)
   (onSubmit: ParentOnSubmitHandler)
   (selectedCardId: CardId)
   (employee: Employee)
   =
   EmployeeFormContainer
      { Locked = false }
      (form employee selectedCardId (InitiatedById session.EmployeeId))
      onSubmit
      None

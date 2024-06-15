module Bank.Account.Forms.CardAccess

open Feliz
open Fable.Form.Simple

open Lib.SharedTypes
open Bank.Employee.Domain
open AsyncUtil
open FormContainer

type Values = { Locked: bool }

let form
   (employee: Employee)
   (selectedCardId: CardId)
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
            LockCardCommand.create employee.CompositeId {
               CardId = selectedCardId
               Reference = None
            }
            |> EmployeeCommand.LockCard
         else
            UnlockCardCommand.create employee.CompositeId {
               CardId = selectedCardId
               Reference = None
            }
            |> EmployeeCommand.UnlockCard

      Msg.Submit(FormCommand.Employee cmd, Started)

   Form.succeed onSubmit |> Form.append isLockedField

let CardAccessFormComponent
   (employee: Employee)
   (selectedCardId: CardId)
   (onSubmit: ParentOnSubmitHandler)
   =
   FormContainer
      (FormDomain.Employee employee)
      { Locked = false }
      (form employee selectedCardId)
      onSubmit

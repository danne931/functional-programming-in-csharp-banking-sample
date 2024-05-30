module Bank.Account.Forms.CardAccess

open Feliz
open Fable.Form.Simple

open Bank.Account.Domain
open AsyncUtil
open FormContainer

type Values = { Locked: bool }

let form (account: Account) : Form.Form<Values, Msg<Values>, IReactProperty> =
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
            LockCardCommand.create account.CompositeId { Reference = None }
            |> AccountCommand.LockCard
         else
            UnlockCardCommand.create account.CompositeId { Reference = None }
            |> AccountCommand.UnlockCard

      Msg.Submit(cmd, Started)

   Form.succeed onSubmit |> Form.append isLockedField

let CardAccessFormComponent
   (account: Account)
   (onSubmit: ParentOnSubmitHandler)
   =
   FormContainer account { Locked = account.CardLocked } (form account) onSubmit

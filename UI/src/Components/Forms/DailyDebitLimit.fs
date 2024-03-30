module Bank.Account.Forms.DailyDebitLimitForm

open Feliz
open Fable.Form.Simple

open Bank.Account.Domain
open AsyncUtil
open Lib.Validators
open FormContainer

type Values = { Amount: string }

let form
   (account: AccountState)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let amountField =
      Form.numberField {
         Parser =
            fun (text: string) ->
               amountValidator "Debit amount" (decimal text)
               |> validationErrorsHumanFriendly
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
         LimitDailyDebitsCommand.create account.EntityId { DebitLimit = amount }

      Msg.Submit(AccountCommand.LimitDailyDebits cmd, Started)

   Form.succeed onSubmit |> Form.append amountField

let DailyDebitLimitFormComponent
   (account: AccountState)
   (onSubmit: ParentOnSubmitHandler)
   =
   FormContainer account { Amount = "" } (form account) onSubmit

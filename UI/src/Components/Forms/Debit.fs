module Bank.Account.Forms.DebitForm

open Feliz
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open AsyncUtil
open Lib.Validators
open FormContainer

type Values = { Amount: string; Origin: string }

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
               |> Result.bind (fun amt ->
                  if account.Balance - amt < 0m then
                     Error $"Insufficient Balance ${account.Balance}"
                  elif
                     account.DailyDebitAccrued + amt > account.DailyDebitLimit
                  then
                     Error
                        $"Exceeded Daily Debit Limit ${account.DailyDebitLimit}"
                  else
                     Ok amt)
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
         DebitCommand.create account.EntityId {
            Amount = amount
            Origin = origin
            Reference = None
            Date = DateTime.UtcNow
         }

      Msg.Submit(AccountCommand.Debit cmd, Started)

   Form.succeed onSubmit |> Form.append amountField |> Form.append originField

let DebitFormComponent
   (account: AccountState)
   (onSubmit: ParentOnSubmitHandler)
   =
   FormContainer account { Amount = ""; Origin = "" } (form account) onSubmit

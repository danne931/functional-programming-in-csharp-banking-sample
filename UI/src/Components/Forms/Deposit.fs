module Bank.Account.Forms.DepositForm

open Feliz
open Fable.Form.Simple
open System

open Lib.Validators
open Bank.Account.Domain
open AsyncUtil
open FormContainer

type Values = { Amount: string }

let form (account: Account) : Form.Form<Values, Msg<Values>, IReactProperty> =
   let amountField =
      Form.numberField {
         Parser =
            fun (text: string) ->
               amountValidator "Deposit amount" (decimal text)
               |> validationErrorsHumanFriendly
         Value = fun values -> values.Amount
         Update = fun newValue values -> { values with Amount = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Deposit Cash:"
            Placeholder = "100"
            HtmlAttributes = []
         }
      }

   let onSubmit amount =
      let command =
         DepositCashCommand.create account.CompositeId {
            Amount = amount
            Origin = Some "ATM"
            Date = DateTime.UtcNow
         }

      Msg.Submit(AccountCommand.DepositCash command, Started)

   Form.succeed onSubmit |> Form.append amountField

let DepositFormComponent (account: Account) (onSubmit: ParentOnSubmitHandler) =
   FormContainer account { Amount = "" } (form account) onSubmit

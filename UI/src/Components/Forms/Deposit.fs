module Bank.Account.Forms.DepositForm

open Feliz
open Fable.Form.Simple

open Lib.Validators
open Bank.Account.Domain
open AsyncUtil
open FormContainer

type Values = { Amount: string }

let form (account: Account) : Form.Form<Values, Msg<Values>, IReactProperty> =
   let amountField =
      Form.textField {
         Parser =
            amountValidatorFromString "Deposit amount"
            >> validationErrorsHumanFriendly
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
         }
         |> AccountCommand.DepositCash
         |> FormCommand.Account

      Msg.Submit(command, Started)

   Form.succeed onSubmit |> Form.append amountField

let DepositFormComponent (account: Account) (onSubmit: ParentOnSubmitHandler) =
   FormContainer
      (FormDomain.Account account)
      { Amount = "" }
      (form account)
      onSubmit

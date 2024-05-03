module Bank.Account.Forms.TransferForm

open Feliz
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Transfer.Domain
open AsyncUtil
open Lib.Validators
open FormContainer

type Values = { Amount: string; RecipientId: string }

let form (account: Account) : Form.Form<Values, Msg<Values>, IReactProperty> =
   let options = [
      for recipientLookupKey, recipient in
         (Map.toList account.TransferRecipients) ->
         recipientLookupKey, $"{recipient.FirstName} {recipient.LastName}"
   ]

   let selectField =
      Form.selectField {
         Parser = Ok
         Value = fun values -> values.RecipientId
         Update = fun newValue values -> { values with RecipientId = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Select a recipient:"
            Placeholder = "No recipient selected"
            Options = options
         }
      }

   let amountField =
      Form.numberField {
         Parser =
            fun (text: string) ->
               amountValidator "Transfer amount" (decimal text)
               |> validationErrorsHumanFriendly
               |> Result.bind (fun amt ->
                  if account.Balance - amt < 0m then
                     Result.Error $"Insufficient Balance ${account.Balance}"
                  else
                     Ok amt)
         Value = fun (values: Values) -> values.Amount
         Update = fun newValue values -> { values with Amount = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Transfer Amount:"
            Placeholder = "25"
            HtmlAttributes = []
         }
      }

   let onSubmit selectedId amount =
      let cmd =
         TransferCommand.create account.EntityId {
            Date = DateTime.UtcNow
            Amount = amount
            Recipient = account.TransferRecipients |> Map.find selectedId
            Reference = None
         }

      Msg.Submit(AccountCommand.Transfer cmd, Started)

   Form.succeed onSubmit |> Form.append selectField |> Form.append amountField

let TransferFormComponent (account: Account) (onSubmit: ParentOnSubmitHandler) =
   let recipients = account.TransferRecipients.Values

   let selectedRecipient =
      if Seq.isEmpty recipients then
         ""
      else
         Account.recipientLookupKey (Seq.head recipients)

   FormContainer
      account
      {
         Amount = ""
         RecipientId = selectedRecipient
      }
      (form account)
      onSubmit

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
open Lib.SharedTypes

type Values = { Amount: string; RecipientId: string }

let form (account: Account) : Form.Form<Values, Msg<Values>, IReactProperty> =
   let options =
      [
         for KeyValue(recipientId, recipient) in account.TransferRecipients ->
            let name =
               match recipient with
               | TransferRecipient.Internal o ->
                  o.Nickname |> Option.defaultValue o.Name
               | TransferRecipient.Domestic o ->
                  o.Nickname |> Option.defaultValue o.Name

            string recipientId, name
      ]
      |> List.sortBy snd

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

   let onSubmit (selectedId: string) (amount: decimal) =
      let selectedId = selectedId |> Guid.Parse |> AccountId

      Map.tryFind selectedId account.InternalTransferRecipients
      |> Option.map (fun recipient ->
         let cmd =
            InternalTransferCommand.create account.CompositeId {
               TransferRequestDate = DateTime.UtcNow
               Amount = amount
               RecipientId = recipient.AccountId
               Reference = None
            }

         Msg.Submit(AccountCommand.InternalTransfer cmd, Started))
      |> Option.defaultWith (fun () ->
         let recipient = Map.find selectedId account.DomesticTransferRecipients

         let cmd =
            DomesticTransferCommand.create account.CompositeId {
               TransferRequestDate = DateTime.UtcNow
               Amount = amount
               Recipient = recipient
               Reference = None
            }

         Msg.Submit(AccountCommand.DomesticTransfer cmd, Started))

   Form.succeed onSubmit |> Form.append selectField |> Form.append amountField

let TransferFormComponent (account: Account) (onSubmit: ParentOnSubmitHandler) =
   FormContainer
      account
      { Amount = ""; RecipientId = "" }
      (form account)
      onSubmit

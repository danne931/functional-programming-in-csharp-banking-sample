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

type Values = {
   Amount: string
   RecipientId: string
   Memo: string
}

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
      Form.textField {
         Parser =
            amountValidatorFromString "Transfer amount"
            >> validationErrorsHumanFriendly
            >> Result.bind (fun amt ->
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

   let memoField =
      Form.textField {
         Parser = Ok
         Value = fun (values: Values) -> values.Memo
         Update = fun newValue values -> { values with Memo = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Memo:"
            Placeholder = "Reason for Transfer"
            HtmlAttributes = []
         }
      }

   let onSubmit (selectedId: string) (amount: decimal) (memo: string option) =
      let selectedId = selectedId |> Guid.Parse |> AccountId

      let memo =
         memo
         |> Option.bind (fun memo ->
            if String.IsNullOrWhiteSpace memo then None else Some memo)

      Map.tryFind selectedId account.InternalTransferRecipients
      |> Option.map (fun recipient ->
         let cmd =
            InternalTransferCommand.create account.CompositeId {
               ScheduledDate = DateTime.UtcNow
               Amount = amount
               RecipientId = recipient.AccountId
               Memo = memo
            }
            |> AccountCommand.InternalTransfer
            |> FormCommand.Account

         Msg.Submit(cmd, Started))
      |> Option.defaultWith (fun () ->
         let recipient = Map.find selectedId account.DomesticTransferRecipients

         let cmd =
            DomesticTransferCommand.create account.CompositeId {
               ScheduledDate = DateTime.UtcNow
               Amount = amount
               Sender = {
                  Name = account.Name
                  AccountNumber = account.AccountNumber
                  RoutingNumber = account.RoutingNumber
                  OrgId = account.OrgId
                  AccountId = account.AccountId
               }
               Recipient = recipient
               Memo = memo
            }
            |> AccountCommand.DomesticTransfer
            |> FormCommand.Account

         Msg.Submit(cmd, Started))

   Form.succeed onSubmit
   |> Form.append selectField
   |> Form.append amountField
   |> Form.append (Form.succeed id |> Form.append memoField |> Form.optional)

let TransferFormComponent (account: Account) (onSubmit: ParentOnSubmitHandler) =
   FormContainer
      (FormDomain.Account account)
      {
         Amount = ""
         RecipientId = ""
         Memo = ""
      }
      (form account)
      onSubmit

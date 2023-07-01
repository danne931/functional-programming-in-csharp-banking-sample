module Bank.Transfer.Api

open System
open EventStore.Client
open FSharp.Control
open System.Threading.Tasks

open Lib.Types
open Bank.Transfer.Domain

let domesticTransfer (evt: BankEvent<DebitedTransfer>) = task {
   // Simulate network request to send money to domestic bank
   (*
      do! DomesticTransferService.Handle {
         AccountNumber = cmd.Recipient.Identification
         RoutingNumber = cmd.Recipient.RoutingNumber
         Amount = cmd.Amount
         Timestamp = cmd.Timestamp
      }
      *)
   do! Task.Delay 1000
}

// Simulate network request to send money to international bank
let internationalTransfer (evt: BankEvent<DebitedTransfer>) =
   Task.Delay(1000) |> Task.ofTask

let recipientExistsInternally
   (es: EventStoreClient)
   (recipient: TransferRecipient)
   =
   EventStoreManager.exists
      es
      (recipient.Identification |> Guid |> Account.streamName)

// Simulate network request to verify account in domestic bank
let recipientExistsDomestically (recipient: TransferRecipient) =
   Task.Delay(1000).ContinueWith(fun _ -> true)

// Simulate network request to verify account in international bank
let recipientExistsInternationally (recipient: TransferRecipient) =
   Task.Delay(1000).ContinueWith(fun _ -> true)

let recipientExists (es: EventStoreClient) (recipient: TransferRecipient) =
   match recipient.AccountEnvironment with
   | RecipientAccountEnvironment.Internal ->
      recipientExistsInternally es recipient
   | RecipientAccountEnvironment.Domestic ->
      recipientExistsDomestically recipient
   | RecipientAccountEnvironment.International ->
      recipientExistsInternationally recipient


let thirdPartyBankTransfer (evt: BankEvent<DebitedTransfer>) =
   match evt.Data.Recipient.AccountEnvironment with
   | RecipientAccountEnvironment.Domestic -> domesticTransfer evt
   | RecipientAccountEnvironment.International -> internationalTransfer evt
   | _ ->
      raise (
         Exception
            "Third party transfer requires a domestic or international account."
      )

[<RequireQualifiedAccess>]
module DomesticTransferRecipientActor

open System.Threading.Tasks
open System.Text
open System.Text.Json
open System.Net
open Akkling

open Lib.Types
open BankTypes
open Bank.Transfer.Domain

module Command = TransferResponseToCommand

type Response = {
   AccountNumber: string
   RoutingNumber: string
   Ok: bool
   Reason: string
   AckReceipt: AckReceipt
}

let domesticTransfer
   (evt: BankEvent<TransferPending>)
   : Task<Result<AckReceipt, string>>
   =
   task {
      let msg = {|
         Action = "TransferRequest"
         AccountNumber = evt.Data.Recipient.Identification
         RoutingNumber = evt.Data.Recipient.RoutingNumber
         Amount = evt.Data.DebitedAmount
         Date = evt.Data.Date
         CorrelationId = string evt.CorrelationId
      |}

      let! response =
         TCP.request
            IPAddress.Loopback
            5001
            Encoding.UTF8
            (JsonSerializer.SerializeToUtf8Bytes msg)

      return
         response
         |> Result.bind (fun res ->
            res |> string |> Serialization.deserialize<Response>)
         |> Result.bind (fun res ->
            match res.Ok with
            | true -> Ok res.AckReceipt
            | false -> Error res.Reason)
   }

let ActorName = "domestic_transfer_recipient"

let private issueTransferToRecipient
   (mailbox: Actor<BankEvent<TransferPending>>)
   (evt: BankEvent<TransferPending>)
   =
   task {
      match! domesticTransfer evt with
      | Error errMsg ->
         // TODO: bring in the circuit breaker

         mailbox.Parent<AccountMessage>()
         <! AccountMessage.StateChange(Command.reject evt errMsg)
      | Ok ackReceipt ->
         mailbox.Parent<AccountMessage>()
         <! AccountMessage.StateChange(Command.approve evt ackReceipt)
   }

let start (mailbox: Actor<AccountMessage>) =
   let handler (mailbox: Actor<BankEvent<TransferPending>>) evt =
      (issueTransferToRecipient mailbox evt).Wait()
      Ignore

   spawn mailbox ActorName (props (actorOf2 handler))

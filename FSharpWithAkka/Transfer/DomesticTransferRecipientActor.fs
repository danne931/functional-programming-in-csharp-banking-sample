[<RequireQualifiedAccess>]
module DomesticTransferRecipientActor

open System.Threading.Tasks
open System.Text
open System.Text.Json
open System.Net
open Akkling
open Akka.Actor
open Akka.Pattern

open Lib.ActivePatterns
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

type Message =
   | TransferPending of BankEvent<TransferPending>
   | BreakerClosed
   | BreakerHalfOpen

let start (system: ActorSystem) (breaker: CircuitBreaker) =
   let handler (mailbox: Actor<Message>) (msg: Message) =
      match msg with
      | BreakerHalfOpen ->
         mailbox.Unstash()
         Ignore
      | BreakerClosed ->
         mailbox.UnstashAll()
         Ignore
      | TransferPending evt ->
         if breaker.IsOpen then
            mailbox.Stash()
            Ignore
         else
            try
               breaker.WithSyncCircuitBreaker(fun () ->
                  match (domesticTransfer evt).Result with
                  | Ok ackReceipt ->
                     select mailbox "../account_coordinator"
                     <! AccountCoordinatorMessage.StateChange(
                        Command.approve evt ackReceipt
                     )

                     Ignore
                  | Error errMsg ->
                     match errMsg with
                     | Contains "InvalidAmount"
                     | Contains "InvalidAccountInfo"
                     | Contains "InactiveAccount" ->
                        select mailbox "../account_coordinator"
                        <! AccountCoordinatorMessage.StateChange(
                           Command.reject evt errMsg
                        )

                        Ignore
                     | Contains "Serialization"
                     | Contains "InvalidAction" ->
                        printfn "DomesticTransfer - Corrupt data %A" errMsg
                        // TODO: notify dev team
                        Unhandled // Send to dead letters instead of stashing
                     | Contains "Connection"
                     | _ ->
                        // Intermittent network error
                        mailbox.Stash() // Store message to reprocess later
                        failwith errMsg // Trip the circuit breaker
               )
            with err when true ->
               printfn "Error: %A" err.Message
               Ignore

   let ref = spawn system ActorName (props (actorOf2 handler))

   breaker.OnOpen(fun () -> printfn $"{ActorName} circuit breaker open.")
   |> ignore

   breaker.OnClose(fun () ->
      printfn $"{ActorName} circuit breaker closed."
      ref <! BreakerClosed)
   |> ignore

   breaker.OnHalfOpen(fun () ->
      printfn $"{ActorName} circuit breaker half open."
      ref <! BreakerHalfOpen)
   |> ignore

   ref

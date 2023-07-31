[<RequireQualifiedAccess>]
module DomesticTransferRecipientActor

open System.Threading.Tasks
open System.Text
open System.Text.Json
open System.Net
open Akkling
open Akka.Actor
open Akka.Pattern
open Akka.Routing

open Lib.ActivePatterns
open Lib.Types
open ActorUtil
open BankTypes
open Bank.Transfer.Domain

module Command = TransferResponseToCommand

let private actorName = ActorMetadata.domesticTransfer.Name

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

type Message =
   | TransferPending of BankEvent<TransferPending>
   | BreakerHalfOpen
   | BreakerClosed

let start
   (system: ActorSystem)
   (breaker: CircuitBreaker)
   (broadcaster: AccountBroadcast)
   (accountFac: AccountActorFac)
   =
   let handler (mailbox: Actor<Message>) (msg: Message) =
      match msg with
      | BreakerHalfOpen ->
         mailbox.Unstash()
         Ignore
      | BreakerClosed ->
         printfn "%A: BreakerClosed - unstash all" mailbox.Self.Path
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
                     let msg =
                        AccountMessage.StateChange(
                           Command.approve evt ackReceipt
                        )

                     accountFac.tell evt.EntityId msg

                     mailbox.UnstashAll()

                     Ignore
                  | Error errMsg ->
                     match errMsg with
                     | Contains "InvalidAmount"
                     | Contains "InvalidAccountInfo"
                     | Contains "InactiveAccount" ->
                        let msg =
                           AccountMessage.StateChange(
                              Command.reject evt errMsg
                           )

                        accountFac.tell evt.EntityId msg

                        Ignore
                     | Contains "Serialization"
                     | Contains "InvalidAction" ->
                        printfn "%A Error: Corrupt data %A" actorName errMsg
                        // TODO: Notify dev team
                        Unhandled // Send to dead letters instead of stashing
                     | Contains "Connection"
                     | _ ->
                        // Intermittent network error
                        mailbox.Stash() // Store message to reprocess later
                        failwith errMsg // Trip the circuit breaker
               )
            with err when true ->
               printfn "%s Error: %s" actorName err.Message
               Ignore

   let ref =
      spawn
         system
         actorName
         {
            (props <| actorOf2 handler) with
               Router = Some FromConfig.Instance
         }

   breaker.OnHalfOpen(fun () ->
      printfn "%s: BreakerHalfOpen" actorName

      broadcaster.broadcastCircuitBreaker
         {
            Service = Service.DomesticTransfer
            Status = CircuitBreakerStatus.HalfOpen
         }
      |> ignore

      ref <! BreakerHalfOpen)
   |> ignore

   // The event handler is bound once even though there may be
   // several routees.  Need to preface BreakerClosed message with
   // Broadcast to ensure all routees are informed.
   // Otherwise, only messages stashed on routee $a will be unstashed.
   breaker.OnClose(fun () ->
      printfn "%s: BreakerClosed - broadcast" actorName

      broadcaster.broadcastCircuitBreaker
         {
            Service = Service.DomesticTransfer
            Status = CircuitBreakerStatus.Closed
         }
      |> ignore

      (retype ref) <! Broadcast BreakerClosed)
   |> ignore

   breaker.OnOpen(fun () ->
      printfn "%s: BreakerOpen" actorName

      broadcaster.broadcastCircuitBreaker
         {
            Service = Service.DomesticTransfer
            Status = CircuitBreakerStatus.Open
         }
      |> ignore)
   |> ignore

   ref

[<RequireQualifiedAccess>]
module DomesticTransferRecipientActor

open System
open System.Text
open System.Text.Json
open Akkling
open Akka.Hosting
open Akka.Actor
open FsToolkit.ErrorHandling

open Lib.ActivePatterns
open Lib.Types
open Bank.Account.Domain
open Bank.Transfer.Domain

module Command = TransferResponseToCommand

module private Msg =
   let approve = AccountMessage.StateChange << AccountCommand.ApproveTransfer
   let reject = AccountMessage.StateChange << AccountCommand.RejectTransfer

let private actorName = ActorUtil.ActorMetadata.domesticTransfer.Name

type Response = {
   AccountNumber: string
   RoutingNumber: string
   Ok: bool
   Reason: string
   AckReceipt: AckReceipt
}

type TransferRequest =
   BankEvent<TransferPending> -> TaskResult<Response, string>

let transferRequest
   (evt: BankEvent<TransferPending>)
   : TaskResult<Response, string>
   =
   taskResult {
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
            EnvTransfer.config.MockThirdPartyBank.Host
            EnvTransfer.config.MockThirdPartyBank.Port
            Encoding.UTF8
            (JsonSerializer.SerializeToUtf8Bytes msg)

      return! Serialization.deserialize<Response> response
   }

type Message =
   | TransferPending of BankEvent<TransferPending>
   | TransferResponse of Response * BankEvent<TransferPending>
   | BreakerHalfOpen
   | BreakerClosed

let handleTransfer
   (mailbox: Actor<obj>)
   (requestTransfer: TransferRequest)
   (evt: BankEvent<TransferPending>)
   =
   task {
      let! result = requestTransfer evt

      return
         match result with
         | Ok res -> TransferResponse(res, evt)
         | Error errMsg ->
            match errMsg with
            | Contains "Serialization"
            | Contains "Deserialization" ->
               let errMsg = $"Corrupt data: {errMsg}"
               logError mailbox errMsg

               let res = {
                  AccountNumber = evt.Data.Recipient.Identification
                  RoutingNumber = evt.Data.Recipient.RoutingNumber.Value
                  Ok = false
                  Reason = "CorruptData"
                  AckReceipt = AckReceipt ""
               }

               TransferResponse(res, evt)
            | Contains "Connection"
            | _ ->
               // Intermittent Error: Reprocess message later
               mailbox.Schedule
                  (TimeSpan.FromSeconds 15.)
                  mailbox.Self
                  (TransferPending evt)
               |> ignore

               failwith errMsg // Side Effect: Trip circuit breaker
   }

let actorProps
   (breaker: Akka.Pattern.CircuitBreaker)
   (getAccountRef: ActorUtil.EntityRefGetter<AccountMessage>)
   (getEmailActor: unit -> IActorRef<EmailActor.EmailMessage>)
   (requestTransfer: TransferRequest)
   =
   let handler (mailbox: Actor<obj>) (message: obj) =
      let logError, logInfo = logError mailbox, logInfo mailbox

      match message with
      | :? Message as msg ->
         match msg with
         | BreakerHalfOpen ->
            logInfo "Breaker half open - unstash one"
            mailbox.Unstash()
            Ignore
         | BreakerClosed ->
            logInfo "Breaker closed - unstash all"
            mailbox.UnstashAll()
            Ignore
         | TransferPending evt ->
            if breaker.IsOpen then
               logInfo "Domestic transfer breaker Open - stashing message"
               mailbox.Stash()
               Ignore
            else
               breaker.WithCircuitBreaker(fun () ->
                  handleTransfer mailbox requestTransfer evt)
               |> Async.AwaitTask
               |!> retype mailbox.Self

               Ignore
         | TransferResponse(res, evt) ->
            let accountRef = getAccountRef evt.EntityId

            if res.Ok then
               let msg = Msg.approve <| Command.approve evt res.AckReceipt
               accountRef <! msg

               mailbox.UnstashAll()
               Ignore
            else
               let errMsg = res.Reason

               match errMsg with
               | Contains "CorruptData"
               | Contains "InvalidAction" ->
                  logError $"Transfer API requires code update: {errMsg}"

                  getEmailActor ()
                  <! EmailActor.ApplicationErrorRequiresSupport errMsg

                  Unhandled
               | Contains "InvalidAmount"
               | Contains "InvalidAccountInfo"
               | Contains "InactiveAccount"
               | _ ->
                  let msg = Msg.reject <| Command.reject evt errMsg
                  accountRef <! msg
                  Ignore
      | :? Status.Failure as e ->
         // Only intermittent network issues which we raise exceptions
         // for to trip the circuit breaker should reach here.
         logWarning mailbox $"Intermittent network issue {e}"
         Ignore
      | LifecycleEvent _ -> Ignore
      | msg ->
         logError $"Unknown message {msg}"
         Unhandled

   props <| actorOf2 handler

let start
   (system: ActorSystem)
   (broadcaster: AccountBroadcast)
   (getAccountRef: ActorUtil.EntityRefGetter<AccountMessage>)
   (breaker: Akka.Pattern.CircuitBreaker)
   (router: Akka.Routing.Pool)
   =
   let prop =
      actorProps
         breaker
         getAccountRef
         (fun _ -> EmailActor.get system)
         transferRequest

   let ref = spawn system actorName { prop with Router = Some router }

   breaker.OnHalfOpen(fun () ->
      broadcaster.circuitBreaker {
         Service = CircuitBreakerService.DomesticTransfer
         Status = CircuitBreakerStatus.HalfOpen
         Timestamp = DateTime.UtcNow
      }
      |> ignore

      ref <! BreakerHalfOpen)
   |> ignore

   // The event handler is bound once even though there may be
   // several routees.  Need to preface BreakerClosed message with
   // Broadcast to ensure all routees are informed.
   // Otherwise, only messages stashed on routee $a will be unstashed.
   breaker.OnClose(fun () ->
      broadcaster.circuitBreaker {
         Service = CircuitBreakerService.DomesticTransfer
         Status = CircuitBreakerStatus.Closed
         Timestamp = DateTime.UtcNow
      }
      |> ignore

      retype ref <! Akka.Routing.Broadcast BreakerClosed)
   |> ignore

   breaker.OnOpen(fun () ->
      system.Log.Log(
         Akka.Event.LogLevel.WarningLevel,
         null,
         "Domestic transfer circuit breaker open"
      )

      broadcaster.circuitBreaker {
         Service = CircuitBreakerService.DomesticTransfer
         Status = CircuitBreakerStatus.Open
         Timestamp = DateTime.UtcNow
      }
      |> ignore)
   |> ignore

   ref

let get (system: ActorSystem) : IActorRef<Message> =
   typed
   <| ActorRegistry
      .For(system)
      .Get<ActorUtil.ActorMetadata.DomesticTransferMarker>()

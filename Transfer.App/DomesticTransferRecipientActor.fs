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
open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Transfer.Domain

module Command = TransferTransactionToCommand

module private Msg =
   let progress =
      AccountMessage.StateChange << AccountCommand.UpdateTransferProgress

   let approve = AccountMessage.StateChange << AccountCommand.ApproveTransfer
   let reject = AccountMessage.StateChange << AccountCommand.RejectTransfer

let private actorName = ActorUtil.ActorMetadata.domesticTransfer.Name

let private progressFromResponse (response: TransferServiceResponse) =
   match response.Status with
   | "Complete" -> TransferProgress.Complete
   | status -> TransferProgress.InProgress status

let private declinedReasonFromError (err: string) : TransferDeclinedReason =
   match err with
   | Contains "CorruptData" -> TransferDeclinedReason.CorruptData
   | Contains "InvalidAction" -> TransferDeclinedReason.InvalidAction
   | Contains "InvalidAmount" -> TransferDeclinedReason.InvalidAmount
   | Contains "InvalidAccountInfo" -> TransferDeclinedReason.InvalidAccountInfo
   | Contains "InactiveAccount" -> TransferDeclinedReason.AccountClosed
   | e -> TransferDeclinedReason.Unknown e

let transferRequest action (txn: TransferTransaction) = taskResult {
   let msg = {|
      Action = string action
      AccountNumber = txn.Recipient.Identification
      RoutingNumber = txn.Recipient.RoutingNumber
      Amount = txn.Amount
      Date = txn.Date
      TransactionId = string txn.TransactionId
   |}

   let! response =
      TCP.request
         EnvTransfer.config.MockThirdPartyBank.Host
         EnvTransfer.config.MockThirdPartyBank.Port
         Encoding.UTF8
         (JsonSerializer.SerializeToUtf8Bytes msg)

   return! Serialization.deserialize<TransferServiceResponse> response
}

let handleTransfer
   (mailbox: Actor<obj>)
   (requestTransfer: TransferRequest)
   (action: TransferServiceAction)
   (txn: TransferTransaction)
   =
   task {
      let! result = requestTransfer action txn

      // Intermittent Error:
      // Reprocess transfer request later.
      // Ignore intermittent progress check error since we
      // receive those on a recurring basis.
      let reprocessLater (errMsg: string) =
         match action with
         | TransferServiceAction.TransferRequest ->
            mailbox.Schedule
            <| TimeSpan.FromSeconds 15.
            <| mailbox.Self
            <| DomesticTransferMessage.TransferRequest(action, txn)
            |> ignore
         | _ -> ()

         failwith errMsg // Side Effect: Trip circuit breaker

      return
         match result with
         | Ok res -> DomesticTransferMessage.TransferResponse(res, action, txn)
         | Error err ->
            match err with
            | Err.SerializationError errMsg ->
               let errMsg = $"Corrupt data: {errMsg}"
               logError mailbox errMsg

               let res = {
                  AccountNumber = txn.Recipient.Identification
                  RoutingNumber = txn.Recipient.RoutingNumber
                  Ok = false
                  Status = ""
                  Reason = "CorruptData"
                  TransactionId = string txn.TransactionId
               }

               DomesticTransferMessage.TransferResponse(res, action, txn)
            | Err.NetworkError err -> reprocessLater err.Message
            | err -> reprocessLater (string err)
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
      | :? DomesticTransferMessage as msg ->
         match msg with
         | DomesticTransferMessage.BreakerHalfOpen ->
            logInfo "Breaker half open - unstash one"
            mailbox.Unstash()
            Ignore
         | DomesticTransferMessage.BreakerClosed ->
            logInfo "Breaker closed - unstash all"
            mailbox.UnstashAll()
            Ignore
         | DomesticTransferMessage.TransferRequest(action, txn) ->
            if breaker.IsOpen then
               match action with
               | TransferServiceAction.TransferRequest ->
                  logInfo "Domestic transfer breaker Open - stashing message"
                  mailbox.Stash()
               | TransferServiceAction.ProgressCheck ->
                  logInfo
                     "Domestic transfer breaker Open - ignore progress check"

               Ignore
            else
               breaker.WithCircuitBreaker(fun () ->
                  handleTransfer mailbox requestTransfer action txn)
               |> Async.AwaitTask
               |!> retype mailbox.Self

               Ignore
         | DomesticTransferMessage.TransferResponse(res, action, txn) ->
            let accountRef = getAccountRef txn.SenderAccountId

            if res.Ok then
               mailbox.UnstashAll()

               let progress = progressFromResponse res

               match progress with
               | TransferProgress.Complete ->
                  let msg = Msg.approve <| Command.approve txn
                  accountRef <! msg
               | progress ->
                  let msg = Msg.progress <| Command.progress txn progress

                  match action with
                  | TransferServiceAction.TransferRequest -> accountRef <! msg
                  | TransferServiceAction.ProgressCheck ->
                     let previousProgress = txn.Status

                     if progress <> previousProgress then
                        accountRef <! msg

               Ignore
            else
               let err = declinedReasonFromError res.Reason

               match err with
               | TransferDeclinedReason.CorruptData
               | TransferDeclinedReason.InvalidAction ->
                  logError $"Transfer API requires code update: {err}"

                  getEmailActor ()
                  <! EmailActor.ApplicationErrorRequiresSupport(string err)

                  Unhandled
               | TransferDeclinedReason.InvalidAmount
               | TransferDeclinedReason.InvalidAccountInfo
               | TransferDeclinedReason.AccountClosed
               | _ ->
                  let msg = Msg.reject <| Command.reject txn err
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

      ref <! DomesticTransferMessage.BreakerHalfOpen)
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

      retype ref
      <! Akka.Routing.Broadcast DomesticTransferMessage.BreakerClosed)
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

let get (system: ActorSystem) : IActorRef<DomesticTransferMessage> =
   typed
   <| ActorRegistry
      .For(system)
      .Get<ActorUtil.ActorMetadata.DomesticTransferMarker>()

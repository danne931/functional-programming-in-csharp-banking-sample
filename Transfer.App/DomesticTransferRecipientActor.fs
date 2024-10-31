module DomesticTransferRecipientActor

open System
open System.Text
open System.Text.Json
open Akkling
open Akkling.Cluster.Sharding
open Akka.Hosting
open Akka.Actor
open FsToolkit.ErrorHandling

open Lib.ActivePatterns
open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Transfer.Domain

module Command = DomesticTransferToCommand
type private DeclinedReason = DomesticTransferDeclinedReason

[<RequireQualifiedAccess>]
type DomesticTransferMessage =
   | TransferRequest of DomesticTransferServiceAction * DomesticTransfer
   | TransferResponse of
      DomesticTransferServiceResponse *
      DomesticTransferServiceAction *
      DomesticTransfer
   | BreakerHalfOpen
   | BreakerClosed

module private Msg =
   let progress =
      AccountMessage.StateChange
      << AccountCommand.UpdateDomesticTransferProgress

   let approve =
      AccountMessage.StateChange << AccountCommand.ApproveDomesticTransfer

   let reject =
      AccountMessage.StateChange << AccountCommand.RejectDomesticTransfer

let private actorName = ActorUtil.ActorMetadata.domesticTransfer.Name

let private progressFromResponse (response: DomesticTransferServiceResponse) =
   match response.Status with
   | "Complete" -> DomesticTransferProgress.Complete
   | status -> DomesticTransferProgress.InProgress status

let private declinedReasonFromError (err: string) : DeclinedReason =
   match err with
   | Contains "CorruptData" -> DeclinedReason.CorruptData
   | Contains "InvalidAction" -> DeclinedReason.InvalidAction
   | Contains "InvalidAmount" -> DeclinedReason.InvalidAmount
   | Contains "InvalidAccountInfo" -> DeclinedReason.InvalidAccountInfo
   | Contains "InvalidPaymentNetwork" -> DeclinedReason.InvalidPaymentNetwork
   | Contains "InvalidDepository" -> DeclinedReason.InvalidDepository
   | Contains "InactiveAccount" -> DeclinedReason.AccountClosed
   | e -> DeclinedReason.Unknown e

let private networkSender
   (sender: DomesticTransferSender)
   : DomesticTransferServiceSender
   =
   {
      Name = sender.Name
      AccountNumber = string sender.AccountNumber
      RoutingNumber = string sender.RoutingNumber
   }

let private networkRecipient
   (recipient: DomesticTransferRecipient)
   : DomesticTransferServiceRecipient
   =
   {
      Name = recipient.Name
      AccountNumber = string recipient.AccountNumber
      RoutingNumber = string recipient.RoutingNumber
      Depository =
         match recipient.Depository with
         | DomesticRecipientAccountDepository.Checking -> "checking"
         | DomesticRecipientAccountDepository.Savings -> "savings"
   }

let handleTransfer
   (mailbox: Actor<obj>)
   (requestTransfer: DomesticTransferRequest)
   (action: DomesticTransferServiceAction)
   (txn: DomesticTransfer)
   =
   task {
      let! result = requestTransfer action txn

      // Intermittent Error:
      // Reprocess transfer request later.
      // Ignore intermittent progress check error since we
      // receive those on a recurring basis.
      let reprocessLater (errMsg: string) =
         match action with
         | DomesticTransferServiceAction.TransferRequest ->
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
                  Sender = networkSender txn.Sender
                  Recipient = networkRecipient txn.Recipient
                  Ok = false
                  Status = ""
                  Reason = "CorruptData"
                  TransactionId = string txn.TransferId
               }

               DomesticTransferMessage.TransferResponse(res, action, txn)
            | Err.NetworkError err -> reprocessLater err.Message
            | err -> reprocessLater (string err)
   }

let actorProps
   (breaker: Akka.Pattern.CircuitBreaker)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (getEmailActor: unit -> IActorRef<EmailActor.EmailMessage>)
   (requestTransfer: DomesticTransferRequest)
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
               | DomesticTransferServiceAction.TransferRequest ->
                  logInfo "Domestic transfer breaker Open - stashing message"
                  mailbox.Stash()
               | DomesticTransferServiceAction.ProgressCheck ->
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
            let accountRef = getAccountRef txn.Sender.AccountId

            if res.Ok then
               mailbox.UnstashAll()

               let progress = progressFromResponse res

               match progress with
               | DomesticTransferProgress.Complete ->
                  let msg = Msg.approve <| Command.approve txn
                  accountRef <! msg
               | DomesticTransferProgress.InProgress progress ->
                  let msg = Msg.progress <| Command.progress txn progress

                  match action with
                  | DomesticTransferServiceAction.TransferRequest ->
                     accountRef <! msg
                  | DomesticTransferServiceAction.ProgressCheck ->
                     let previousProgress = txn.Status

                     if
                        (DomesticTransferProgress.InProgress progress)
                        <> previousProgress
                     then
                        accountRef <! msg
               | _ -> ()

               Ignore
            else
               let err = declinedReasonFromError res.Reason

               match err with
               | DeclinedReason.CorruptData
               | DeclinedReason.InvalidPaymentNetwork
               | DeclinedReason.InvalidDepository
               | DeclinedReason.InvalidAction ->
                  logError $"Transfer API requires code update: {err}"

                  getEmailActor ()
                  <! EmailActor.EmailMessage.ApplicationErrorRequiresSupport(
                     string err,
                     txn.Sender.OrgId
                  )

                  Unhandled
               | DeclinedReason.InvalidAmount
               | DeclinedReason.InvalidAccountInfo
               | DeclinedReason.AccountClosed
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

let transferRequest action (txn: DomesticTransfer) = taskResult {
   let msg = {|
      Action = string action
      Sender = networkSender txn.Sender
      Recipient = networkRecipient txn.Recipient
      Amount = txn.Amount
      Date = txn.ScheduledDate
      TransactionId = string txn.TransferId
      PaymentNetwork =
         match txn.Recipient.PaymentNetwork with
         | PaymentNetwork.ACH -> "ach"
   |}

   let! response =
      TCP.request
         EnvTransfer.config.MockDomesticTransferProcessor.Host
         EnvTransfer.config.MockDomesticTransferProcessor.Port
         Encoding.UTF8
         (JsonSerializer.SerializeToUtf8Bytes msg)

   return! Serialization.deserialize<DomesticTransferServiceResponse> response
}

let start
   (system: ActorSystem)
   (broadcaster: AccountBroadcast)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
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

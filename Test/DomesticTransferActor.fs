module DomesticTransferActorTests

open System
open System.Threading.Tasks
open Akka.Actor
open Akkling
open Akkling.Cluster.Sharding
open Expecto

open Util
open ActorUtil
open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Transfer.Domain

let initMockAccountActor (tck: TestKit.Tck) =
   let handler (ctx: Actor<_>) (msg: obj) =
      match msg with
      | :? ShardEnvelope as envelope ->
         match envelope.Message with
         | :? AccountMessage as msg ->
            match msg with
            | AccountMessage.StateChange msg ->
               match msg with
               | UpdateTransferProgress cmd ->
                  tck.TestActor.Tell cmd
                  ignored ()
               | RejectTransfer cmd ->
                  tck.TestActor.Tell cmd
                  ignored ()
               | ApproveTransfer cmd ->
                  tck.TestActor.Tell cmd
                  ignored ()
               | msg -> unhandled msg
            | msg -> unhandled msg
         | msg -> unhandled msg
      | msg -> unhandled msg

   spawnAnonymous tck <| props (actorOf2 handler)

// Will fail with an intermittent "Connection" error
// for the 2nd and 3rd request received.
let mockTransferRequestFactory () : TransferRequest =
   let mutable requestCount = 0

   fun (action: TransferServiceAction) (txn: TransferTransaction) ->
      requestCount <- requestCount + 1

      if requestCount > 2 && requestCount <= 4 then
         "Connection terminated"
         |> exn
         |> Err.NetworkError
         |> Error
         |> Task.FromResult
      else
         let status =
            match action with
            | TransferServiceAction.TransferRequest -> "Processing"
            | TransferServiceAction.ProgressCheck -> "Complete"

         let response: TransferServiceResponse = {
            AccountNumber = ""
            RoutingNumber = None
            Ok = true
            Reason = ""
            Status = status
            TransactionId = string <| Guid.NewGuid()
         }

         response |> Ok |> Task.FromResult

let mockTransferRequestInvalid
   (action: TransferServiceAction)
   (txn: TransferTransaction)
   =
   let response: TransferServiceResponse = {
      AccountNumber = ""
      RoutingNumber = None
      Ok = false
      Reason = "InvalidAction"
      Status = ""
      TransactionId = string <| Guid.NewGuid()
   }

   response |> Ok |> Task.FromResult

let mockTransferRequestInactiveAccount
   (action: TransferServiceAction)
   (txn: TransferTransaction)
   =
   let response: TransferServiceResponse = {
      AccountNumber = ""
      RoutingNumber = None
      Ok = false
      Reason = "InactiveAccount"
      Status = ""
      TransactionId = string <| Guid.NewGuid()
   }

   response |> Ok |> Task.FromResult

let mockTransferRequestSerializationIssue
   (action: TransferServiceAction)
   (txn: TransferTransaction)
   =
   "Serialization" |> Err.SerializationError |> Result.Error |> Task.FromResult

let initCircuitBreaker (tck: TestKit.Tck) =
   let breaker =
      Akka.Pattern.CircuitBreaker(
         tck.Sys.Scheduler,
         maxFailures = 2,
         callTimeout = TimeSpan.FromSeconds 1,
         resetTimeout = TimeSpan.FromMilliseconds 100
      )

   // TODO:
   // OnHalfOpen/OnClose is not being triggered in test environment
   // despite a resetTimeout of 100ms.
   // Not sure how to test reprocessing of stashed transfers
   // if can't hook into HalfOpen or Closed status or initiate
   // them manually.
   (*
   breaker.OnHalfOpen(fun () -> 
      tck.TestActor.Tell CircuitBreakerStatus.HalfOpen
   )
   |> ignore
   *)

   breaker.OnOpen(fun () -> tck.TestActor.Tell CircuitBreakerStatus.Open)

let initDomesticTransferActor
   (tck: TestKit.Tck)
   (breaker: Akka.Pattern.CircuitBreaker)
   (getAccountActor: AccountId -> IEntityRef<AccountMessage>)
   (emailActor: IActorRef<EmailActor.EmailMessage>)
   (transferRequest: TransferRequest)
   =
   let prop =
      DomesticTransferRecipientActor.actorProps
         breaker
         getAccountActor
         (fun _ -> emailActor)
         transferRequest

   spawn tck ActorMetadata.domesticTransfer.Name prop

let init (tck: TestKit.Tck) (mockTransferRequest: TransferRequest) =
   let breaker = initCircuitBreaker tck
   let mockAccountRef = initMockAccountActor tck

   let emailProbe = TestKit.probe tck

   let domesticTransferRef =
      initDomesticTransferActor tck breaker
      <| getAccountEntityRef mockAccountRef
      <| (typed emailProbe :> IActorRef<EmailActor.EmailMessage>)
      <| mockTransferRequest

   domesticTransferRef, breaker, emailProbe

[<Tests>]
let tests =
   testList "Domestic Transfer Actor" [
      akkaTest
         "Issuing a transfer for a valid 3rd party bank recipient should approve the transfer"
      <| None
      <| fun tck ->
         let domesticTransferRef, _, _ =
            init tck <| mockTransferRequestFactory ()

         let evt = Stub.event.domesticTransferPending
         let txn = TransferEventToTransaction.fromPending evt

         domesticTransferRef
         <! DomesticTransferMessage.TransferRequest(
            TransferServiceAction.TransferRequest,
            txn
         )

         let cmd = tck.ExpectMsg<UpdateTransferProgressCommand>()

         Expect.equal
            (AccountId.fromEntityId cmd.EntityId)
            txn.SenderAccountId
            $"SenderAccountId from Transfer Transaction should be
              EntityId of resulting UpdateTransferProgressCommand"

         let evt =
            Expect.wantOk
               (UpdateTransferProgressCommand.toEvent cmd)
               "TransferProgress cmd -> event validation ok"

         let txn = TransferEventToTransaction.fromProgressUpdate evt

         domesticTransferRef
         <! DomesticTransferMessage.TransferRequest(
            TransferServiceAction.ProgressCheck,
            txn
         )

         let cmd = tck.ExpectMsg<ApproveTransferCommand>()

         Expect.equal
            (AccountId.fromEntityId cmd.EntityId)
            txn.SenderAccountId
            $"SenderAccountId from Transfer Transaction should be
              EntityId of resulting ApproveTransferCommand"

      akkaTest
         "Issuing a transfer for an inactive 3rd party bank recipient should
          reject the transfer"
      <| None
      <| fun tck ->
         let domesticTransferRef, _, _ =
            init tck mockTransferRequestInactiveAccount

         let evt = Stub.event.domesticTransferPending
         let txn = TransferEventToTransaction.fromPending evt

         domesticTransferRef
         <! DomesticTransferMessage.TransferRequest(
            TransferServiceAction.TransferRequest,
            txn
         )

         let msg = tck.ExpectMsg<RejectTransferCommand>()

         Expect.equal
            (AccountId.fromEntityId msg.EntityId)
            txn.SenderAccountId
            $"SenderAccountId from Transaction should be
              EntityId of resulting RejectTransferCommand"

      akkaTest
         "Issuing a transfer request with an API mismatch should email support."
      <| None
      <| fun tck ->
         let domesticTransferRef, _, emailProbe =
            init tck mockTransferRequestInvalid

         let subscriber = tck.CreateTestProbe()

         tck.Sys.EventStream.Subscribe(
            subscriber.Ref,
            typeof<Akka.Event.UnhandledMessage>
         )
         |> ignore

         let evt = Stub.event.domesticTransferPending
         let txn = TransferEventToTransaction.fromPending evt

         domesticTransferRef
         <! DomesticTransferMessage.TransferRequest(
            TransferServiceAction.TransferRequest,
            txn
         )

         let msg = emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.ApplicationErrorRequiresSupport _ ->
            Expect.isTrue true ""
         | msg ->
            Expect.isTrue
               false
               $"Expected EmailMessage.ApplicationErrorRequiresSupport.
                 Received {msg}"

         tck.ExpectNoMsg()
         subscriber.ExpectMsg<Akka.Event.UnhandledMessage>() |> ignore

      akkaTest
         "Experiencing serialization issues for transfer request should email support."
      <| None
      <| fun tck ->
         let domesticTransferRef, _, emailProbe =
            init tck mockTransferRequestSerializationIssue

         let subscriber = tck.CreateTestProbe()

         tck.Sys.EventStream.Subscribe(
            subscriber.Ref,
            typeof<Akka.Event.UnhandledMessage>
         )
         |> ignore

         let evt = Stub.event.domesticTransferPending
         let txn = TransferEventToTransaction.fromPending evt

         domesticTransferRef
         <! DomesticTransferMessage.TransferRequest(
            TransferServiceAction.TransferRequest,
            txn
         )

         let msg = emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.ApplicationErrorRequiresSupport _ ->
            Expect.isTrue true ""
         | msg ->
            Expect.isTrue
               false
               $"Expected EmailMessage.ApplicationErrorRequiresSupport.
                 Received {msg}"

         tck.ExpectNoMsg()
         subscriber.ExpectMsg<Akka.Event.UnhandledMessage>() |> ignore

      akkaTest
         "Issuing a transfer for a valid 3rd party bank recipient
          should open the circuit breaker for intermittent failures."
      <| None
      <| fun tck ->
         let domesticTransferRef, breaker, _ =
            init tck <| mockTransferRequestFactory ()

         let evt = Stub.event.domesticTransferPending
         let txn = TransferEventToTransaction.fromPending evt

         let transferMsg =
            DomesticTransferMessage.TransferRequest(
               TransferServiceAction.TransferRequest,
               txn
            )

         domesticTransferRef <! transferMsg

         let cmd = tck.ExpectMsg<UpdateTransferProgressCommand>()

         let evt =
            Expect.wantOk
               (UpdateTransferProgressCommand.toEvent cmd)
               "TransferProgress cmd -> event validation ok"

         let txn = TransferEventToTransaction.fromProgressUpdate evt

         domesticTransferRef
         <! DomesticTransferMessage.TransferRequest(
            TransferServiceAction.ProgressCheck,
            txn
         )

         // 1st message succeeds
         tck.ExpectMsg<ApproveTransferCommand>() |> ignore

         // Message 2 & 3 fail, opening the circuit breaker &
         // stashing corresponding messages.  Subsequent messages
         // are stashed for later processing without attempting to make
         // a request.
         domesticTransferRef <! transferMsg
         domesticTransferRef <! transferMsg

         tck.AwaitAssert(fun () ->
            Expect.equal
               breaker.CurrentFailureCount
               2
               "Expected circuit breaker fail count = 2")

         TestKit.expectMsg tck CircuitBreakerStatus.Open |> ignore

         domesticTransferRef <! transferMsg

         tck.AwaitAssert(fun () ->
            Expect.equal
               breaker.CurrentFailureCount
               2
               "Circuit breaker fail count still = 2 since subsequent messages 
                received after the breaker opens are stashed for later processing")
   ]

module DomesticTransferActorTests

open System
open System.Threading.Tasks
open Akka.Actor
open Akkling
open Akkling.Cluster.Sharding
open Expecto
open FsToolkit.ErrorHandling

open Util
open ActorUtil
open Lib.Types
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
let mockTransferRequestFactory () =
   let mutable requestCount = 0

   fun (evt: BankEvent<TransferPending>) ->
      requestCount <- requestCount + 1

      if requestCount > 1 && requestCount <= 3 then
         "Connection" |> Error |> Task.FromResult
      else
         let response: DomesticTransferRecipientActor.Response = {
            AccountNumber = ""
            RoutingNumber = ""
            Ok = true
            Reason = ""
            AckReceipt = AckReceipt(string evt.EntityId)
         }

         response |> Ok |> Task.FromResult

let mockTransferRequestInvalid (evt: BankEvent<TransferPending>) =
   let response: DomesticTransferRecipientActor.Response = {
      AccountNumber = ""
      RoutingNumber = ""
      Ok = false
      Reason = "InvalidAction"
      AckReceipt = AckReceipt(string evt.EntityId)
   }

   response |> Ok |> Task.FromResult

let mockTransferRequestInactiveAccount (_: BankEvent<TransferPending>) =
   let response: DomesticTransferRecipientActor.Response = {
      AccountNumber = ""
      RoutingNumber = ""
      Ok = false
      Reason = "InactiveAccount"
      AckReceipt = AckReceipt("")
   }

   response |> Ok |> Task.FromResult

let mockTransferRequestSerializationIssue (_: BankEvent<TransferPending>) =
   Error "Serialization" |> Task.FromResult

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
   (getAccountActor: EntityRefGetter<AccountMessage>)
   (emailActor: IActorRef<EmailActor.EmailMessage>)
   (transferRequest:
      BankEvent<TransferPending>
         -> TaskResult<DomesticTransferRecipientActor.Response, string>)
   =
   let prop =
      DomesticTransferRecipientActor.actorProps
         breaker
         getAccountActor
         (fun _ -> emailActor)
         transferRequest

   spawn tck ActorMetadata.domesticTransfer.Name prop

let init
   (tck: TestKit.Tck)
   (mockTransferRequest:
      BankEvent<TransferPending>
         -> TaskResult<DomesticTransferRecipientActor.Response, string>)
   =
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

         domesticTransferRef
         <! DomesticTransferRecipientActor.Message.TransferPending evt

         let msg = tck.ExpectMsg<ApproveTransferCommand>()

         Expect.equal
            msg.EntityId
            evt.EntityId
            $"EntityId from TransferPending event should be
              EntityId of resulting ApproveTransferCommand"

      akkaTest
         "Issuing a transfer for an inactive 3rd party bank recipient should
          reject the transfer"
      <| None
      <| fun tck ->
         let domesticTransferRef, _, _ =
            init tck mockTransferRequestInactiveAccount

         let evt = Stub.event.domesticTransferPending

         domesticTransferRef
         <! DomesticTransferRecipientActor.Message.TransferPending evt

         let msg = tck.ExpectMsg<RejectTransferCommand>()

         Expect.equal
            msg.EntityId
            evt.EntityId
            $"EntityId from TransferPending event should be
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

         domesticTransferRef
         <! DomesticTransferRecipientActor.Message.TransferPending evt

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

         domesticTransferRef
         <! DomesticTransferRecipientActor.Message.TransferPending evt

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
         let msg = DomesticTransferRecipientActor.Message.TransferPending evt

         domesticTransferRef <! msg

         // 1st message succeeds
         tck.ExpectMsg<ApproveTransferCommand>() |> ignore

         // Message 2 & 3 fail, opening the circuit breaker &
         // stashing corresponding messages.  Subsequent messages
         // are stashed for later processing without attempting to make
         // a request.
         domesticTransferRef <! msg
         domesticTransferRef <! msg

         tck.AwaitAssert(fun () ->
            Expect.equal
               breaker.CurrentFailureCount
               2
               "Expected circuit breaker fail count = 2")

         TestKit.expectMsg tck CircuitBreakerStatus.Open |> ignore

         domesticTransferRef <! msg

         tck.AwaitAssert(fun () ->
            Expect.equal
               breaker.CurrentFailureCount
               2
               "Circuit breaker fail count still = 2 since subsequent messages 
                received after the breaker opens are stashed for later processing")
   ]

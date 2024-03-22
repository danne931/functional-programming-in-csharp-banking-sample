[<RequireQualifiedAccess>]
module CircuitBreakerActor

open Akka.Actor
open Akka.Hosting
open Akka.Pattern
open Akkling
open Akkling.Persistence

open ActorUtil
open Lib.SharedTypes
open Lib.Types

let private persistAsync (msg: CircuitBreakerEvent) = msg |> box |> PersistAsync

let private apply (state: CircuitBreakerActorState) (evt: CircuitBreakerEvent) =
   match evt.Service with
   | CircuitBreakerService.DomesticTransfer -> {
      state with
         DomesticTransfer = evt.Status
     }
   | CircuitBreakerService.Email -> { state with Email = evt.Status }

let actorProps () =
   let handler (mailbox: Eventsourced<obj>) =
      let rec loop (state: CircuitBreakerActorState) = actor {
         let! msg = mailbox.Receive()

         return!
            match msg with
            | :? CircuitBreakerEvent as e when mailbox.IsRecovering() ->
               loop <| apply state e
            | :? CircuitBreakerMessage as msg ->
               match msg with
               | CircuitBreakerMessage.Lookup ->
                  mailbox.Sender() <! state
                  ignored ()
               | CircuitBreakerMessage.CircuitBreaker evt ->
                  match evt.Service with
                  | CircuitBreakerService.DomesticTransfer ->
                     if evt.Status = state.DomesticTransfer then
                        ignored ()
                     else
                        loop {
                           state with
                              DomesticTransfer = evt.Status
                        }
                        <@> persistAsync evt
                  | CircuitBreakerService.Email ->
                     if evt.Status = state.Email then
                        ignored ()
                     else
                        loop { state with Email = evt.Status }
                        <@> persistAsync evt
            | Persisted mailbox _ -> ignored ()
            | msg ->
               PersistentActorEventHandler.handleEvent
                  PersistentActorEventHandler.init
                  mailbox
                  msg
      }

      loop {
         DomesticTransfer = CircuitBreakerStatus.Closed
         Email = CircuitBreakerStatus.Closed
      }

   propsPersist handler

let initProps (opts: BackoffSupervisorOptions) =
   BackoffSupervisor.Props(
      Backoff
         .OnFailure(
            actorProps().ToProps(),
            $"{ActorMetadata.circuitBreaker.Name}-worker",
            minBackoff = opts.MinBackoff,
            maxBackoff = opts.MaxBackoff,
            randomFactor = opts.RandomFactor,
            maxNrOfRetries = opts.MaxNrOfRetries
         )
         .WithAutoReset(opts.ResetCounterAfter)
   )

let get (system: ActorSystem) : IActorRef<CircuitBreakerMessage> =
   ActorRegistry.For(system).Get<ActorMetadata.CircuitBreakerMarker>() |> typed

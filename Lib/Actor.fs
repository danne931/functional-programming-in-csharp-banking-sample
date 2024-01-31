module ActorUtil

open System
open System.Threading.Tasks
open Akkling
open Akkling.Cluster.Sharding
open Akkling.Persistence
open Akka.Streams
open Akkling.Streams
open Akka.Actor
open Akka.Cluster.Sharding
open Akka.Persistence
open Akka.Persistence.Query
open Akka.Persistence.Extras
open Akka.Persistence.Sql.Query
open Akka.Cluster.Tools.PublishSubscribe

open Lib.Types

type EntityRefGetter<'t> = Guid -> IEntityRef<'t>

module SystemLog =
   let info (sys: ActorSystem) (msg: string) =
      sys.Log.Log(Akka.Event.LogLevel.InfoLevel, null, msg)

   let warning (sys: ActorSystem) (msg: string) =
      sys.Log.Log(Akka.Event.LogLevel.WarningLevel, null, msg)

   let error (sys: ActorSystem) (err: exn) (msg: string) =
      sys.Log.Log(Akka.Event.LogLevel.ErrorLevel, err, msg)

let registerSelfForPubSub (ctx: Actor<_>) =
   DistributedPubSub.Get(ctx.System).Mediator.Tell(Put(untyped ctx.Self))

let getChildActorRef<'t, 'r>
   (actorCtx: Actor<'t>)
   (name: string)
   : IActorRef<'r> option
   =
   let accountRef = actorCtx.UntypedContext.Child(name)

   match accountRef = ActorRefs.Nobody with
   | true -> None
   | false -> Some(typed accountRef)

let messageExtractor maxNumberOfShards =
   HashCodeMessageExtractor.Create(
      maxNumberOfShards = maxNumberOfShards,
      entityIdExtractor =
         (fun msg ->
            match msg with
            | :? ShardEnvelope as e -> e.EntityId
            | _ -> null),
      messageExtractor =
         fun msg ->
            match msg with
            | :? ShardEnvelope as e -> e.Message
            | _ -> msg
   )

module ClusterMetadata =
   type ShardRegion = {
      name: string
      messageExtractor: IMessageExtractor
   }

   let accountShardRegion = {
      name = "account"
      // TODO: Figure out ideal max #
      messageExtractor = messageExtractor 1000
   }

   let roles = {|
      web = "web-role"
      account = "account-role"
      signalR = "signal-r-role"
      scheduling = "scheduling-role"
   |}

module ActorMetadata =
   type AccountEventConsumerMarker() =
      class
      end

   type CircuitBreakerMarker() =
      class
      end

   type AccountLoadTestMarker() =
      class
      end

   type AuditorMarker() =
      class
      end

   type AccountSeederMarker() =
      class
      end

   type EmailMarker() =
      class
      end

   type AccountClosureMarker() =
      class
      end

   type DomesticTransferMarker() =
      class
      end

   type TransferProgressTrackingMarker() =
      class
      end

   type BillingCycleMarker() =
      class
      end

   type BillingStatementMarker() =
      class
      end

   type AccountMarker() =
      class
      end

   type SchedulingMarker() =
      class
      end

   type ActorMetadata = {
      Name: string
      Route: string
   } with

      member x.Path = ActorPath.Parse $"akka://bank/user/{x.Route}"

      member x.SingletonPath =
         ActorPath.Parse $"akka://bank/user/{x.Route}/{x.Route}"

      member x.ProxyPath = ActorPath.Parse $"akka://bank/user/{x.Route}-proxy"

   type ChildEntityActorMetadata = {
      Name: string
      RouteBuilder: string -> string
   } with

      member x.Path(entityId: string) =
         ActorPath.Parse $"akka://bank/user/{x.RouteBuilder entityId}"

   let account = { Name = "account"; Route = "account" }

   let accountClosure = {
      Name = "account-closure"
      Route = "account-closure"
   }

   let accountEventConsumer = {
      Name = "account-event-consumer"
      Route = "account-event-consumer"
   }

   let internalTransfer = {
      Name = "internal-transfer-recipient"
      RouteBuilder =
         fun accountId -> $"account/{accountId}/internal-transfer-recipient"
   }

   let domesticTransfer = {
      Name = "domestic-transfer-recipient"
      Route = "domestic-transfer-recipient"
   }

   let transferProgressTracking = {
      Name = "transfer-progress-tracking"
      Route = "transfer-progress-tracking"
   }

   let billingCycle = {
      Name = "billing-cycle"
      Route = "billing-cycle"
   }

   let billingStatement = {
      Name = "billing-statement"
      Route = "billing-statement"
   }

   let email = { Name = "email"; Route = "email" }

   let auditor = { Name = "auditor"; Route = "auditor" }

   let accountSeeder = {
      Name = "account-seeder"
      Route = "account-seeder"
   }

   let accountLoadTest = {
      Name = "account-load-test"
      Route = "account-load-test"
   }

   let signalR = {
      Name = "signal-r"
      Route = "signal-r"
   }

   let scheduling = {
      Name = "scheduling"
      Route = "scheduling"
   }

   let circuitBreaker = {
      Name = "circuit-breaker"
      Route = "circuit-breaker"
   }

let readJournal (system: ActorSystem) : SqlReadJournal =
   PersistenceQuery
      .Get(system)
      .ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier)

let getEntityRef
   system
   (shardRegionMeta: ClusterMetadata.ShardRegion)
   (entityId: Guid)
   : IEntityRef<'t>
   =
   let fac = {
      TypeName = shardRegionMeta.name
      ShardRegion =
         ClusterSharding.Get(system).ShardRegion(shardRegionMeta.name)
   }

   let shardId = shardRegionMeta.messageExtractor.ShardId(string entityId)

   fac.RefFor shardId <| string entityId

module PersistentActorEventHandler =
   type T = {
      LifecyclePreStart: Eventsourced<obj> -> Effect<obj>
      LifecyclePostStop: Eventsourced<obj> -> Effect<obj>
      LifecyclePreRestart: Eventsourced<obj> -> exn -> obj -> Effect<obj>
      LifecyclePostRestart: Eventsourced<obj> -> exn -> Effect<obj>
      RecoveryCompleted: Eventsourced<obj> -> Effect<obj>
      ReplaySucceed: Eventsourced<obj> -> Effect<obj>
      // After failure handler invoked, actor is stopped
      ReplayFailed: Eventsourced<obj> -> exn -> obj -> Effect<obj>
      // After rejection handler invoked, no automatic actions
      PersistRejected: Eventsourced<obj> -> exn -> obj -> int64 -> Effect<obj>
      // After failure handler invoked, actor is stopped
      PersistFailed: Eventsourced<obj> -> exn -> obj -> int64 -> Effect<obj>
      DeleteMessagesSuccess: Eventsourced<obj> -> Effect<obj>
      // After failure handler invoked, no automatic actions
      DeleteMessagesFailure:
         Eventsourced<obj> -> DeleteMessagesFailure -> Effect<obj>
      SaveSnapshotSuccess:
         Eventsourced<obj> -> SaveSnapshotSuccess -> Effect<obj>
      SaveSnapshotFailure:
         Eventsourced<obj> -> SaveSnapshotFailure -> Effect<obj>
      DeleteSnapshotSuccess: Eventsourced<obj> -> Effect<obj>
      DeleteSnapshotFailure:
         Eventsourced<obj> -> DeleteSnapshotFailure -> Effect<obj>
      DeleteSnapshotsSuccess: Eventsourced<obj> -> Effect<obj>
      DeleteSnapshotsFailure:
         Eventsourced<obj> -> DeleteSnapshotsFailure -> Effect<obj>
   }

   let init = {
      LifecyclePreStart = fun _ -> ignored ()
      LifecyclePostStop = fun _ -> ignored ()
      LifecyclePreRestart = fun _ _ _ -> ignored ()
      LifecyclePostRestart = fun _ _ -> ignored ()
      RecoveryCompleted = fun _ -> ignored ()
      ReplaySucceed = fun _ -> ignored ()
      ReplayFailed = fun _ _ _ -> ignored ()
      PersistRejected = fun _ _ _ _ -> ignored ()
      PersistFailed = fun _ _ _ _ -> ignored ()
      DeleteMessagesSuccess = fun _ -> ignored ()
      DeleteMessagesFailure = fun _ _ -> ignored ()
      SaveSnapshotSuccess = fun _ _ -> ignored ()
      SaveSnapshotFailure = fun _ _ -> ignored ()
      DeleteSnapshotSuccess = fun _ -> ignored ()
      DeleteSnapshotFailure = fun _ _ -> ignored ()
      DeleteSnapshotsSuccess = fun _ -> ignored ()
      DeleteSnapshotsFailure = fun _ _ -> ignored ()
   }

   let handleEvent
      (handler: T)
      (mailbox: Eventsourced<obj>)
      (msg: obj)
      : Effect<obj>
      =
      match msg with
      | LifecycleEvent e ->
         match e with
         | PreStart ->
            logDebug mailbox "<PreStart>"
            handler.LifecyclePreStart mailbox
         | PostStop ->
            logDebug mailbox "<PostStop>"
            handler.LifecyclePostStop mailbox
         | PreRestart(err: exn, msg: obj) ->
            logDebug mailbox $"<PreRestart>: {err} - {msg}"
            handler.LifecyclePreRestart mailbox err msg
         | PostRestart(err: exn) ->
            logDebug mailbox $"<PostRestart>: {err}"
            handler.LifecyclePostRestart mailbox err
      | :? Akkling.Persistence.PersistentLifecycleEvent as evt ->
         match evt with
         | ReplaySucceed ->
            logDebug mailbox "<ReplaySucceed>"
            handler.ReplaySucceed mailbox
         | ReplayFailed(err: exn, msg: obj) ->
            logError mailbox $"<ReplayFailed>: %s{err.Message} - {msg}"
            handler.ReplayFailed mailbox err msg
         | PersistRejected(err: exn, evt: obj, sequenceNr: int64) ->
            logError
               mailbox
               $"<PersistRejected>: %s{err.Message} - Sequence #{sequenceNr} - {evt}"

            handler.PersistRejected mailbox err evt sequenceNr
         | PersistFailed(err: exn, evt: obj, sequenceNr: int64) ->
            logError
               mailbox
               $"<PersistFailed>: %s{err.Message} - Sequence #{sequenceNr} - {evt}"

            handler.PersistFailed mailbox err evt sequenceNr
      | :? Akka.Persistence.RecoveryCompleted ->
         logDebug mailbox "<RecoveryCompleted>"
         handler.RecoveryCompleted mailbox
      | :? DeleteMessagesSuccess ->
         logDebug mailbox "<DeleteMessagesSuccess>"
         handler.DeleteMessagesSuccess mailbox
      | :? DeleteMessagesFailure as err ->
         logError mailbox $"<DeleteMessagesFailure>: {err.Cause}"
         handler.DeleteMessagesFailure mailbox err
      | :? SaveSnapshotSuccess as res ->
         logDebug mailbox $"<SaveSnapshotSuccess>: {res.Metadata}"
         handler.SaveSnapshotSuccess mailbox res
      | :? SaveSnapshotFailure as err ->
         logError mailbox $"<SaveSnapshotFailure>: {err.Metadata}"
         handler.SaveSnapshotFailure mailbox err
      | :? DeleteSnapshotSuccess ->
         logDebug mailbox "<DeleteSnapshotSuccess>"
         handler.DeleteSnapshotSuccess mailbox
      | :? DeleteSnapshotFailure as err ->
         logError mailbox $"<DeleteSnapshotFailure>: {err.Cause}"
         handler.DeleteSnapshotFailure mailbox err
      | :? DeleteSnapshotsSuccess ->
         logDebug mailbox "<DeleteSnapshotsSuccess>"
         handler.DeleteSnapshotsSuccess mailbox
      | :? DeleteSnapshotsFailure as err ->
         logError mailbox $"<DeleteSnapshotsFailure>: {err.Cause}"
         handler.DeleteSnapshotsFailure mailbox err
      | msg ->
         logError mailbox $"<UnknownMessage>: %s{msg.GetType().FullName}"
         unhandled ()

/// <summary>
/// Add an item to an Akka.Streams queue.
/// </summary>
let queueOffer<'t>
   (queue: ISourceQueueWithComplete<'t>)
   (msg: 't)
   : Result<Effect<obj>, string> Task
   =
   task {
      let! result = queue.AsyncOffer msg

      return
         match result with
         | :? QueueOfferResult.Dropped -> Error "Message dropped"
         | :? QueueOfferResult.Failure as f ->
            Error $"Failed with exception: {f.Cause}"
         | :? QueueOfferResult.QueueClosed -> Error "Queue closed"
         | :? QueueOfferResult.Enqueued -> Ok <| ignored ()
         | _ -> Ok <| unhandled ()
   }

/// <summary>
/// Create props for a parent supervisor actor (which just forwards messages to
/// child) so can configure non-default SupervisorStrategy for top-level actors.
/// </summary>
let supervisorProps
   (spawnChild: Actor<_> -> IActorRef<_>)
   (strategy: Akka.Actor.SupervisorStrategy)
   =
   let init (ctx: Actor<_>) =
      let child = spawnChild ctx

      actor {
         let! msg = ctx.Receive()
         child <<! msg
         return ignored ()
      }

   {
      props init with
         SupervisionStrategy = Some strategy
   }

/// <summary>
/// Create PersistenceSupervisor props to ensure
/// failed Persist calls are retried and messages received
/// during backoff period are not lost.
/// See https://devops.petabridge.com/articles/state-management/akkadotnet-persistence-failure-handling.html
/// for info on why PersistenceSupervisor may be preferred over
/// BackoffSupervisor in persistent actor scenarios.
/// </summary>
let persistenceSupervisor
   (opts: PersistenceSupervisorOptions)
   (isPersistableMessage: obj -> bool)
   (childProps: Props<_>)
   (persistenceId: string)
   =
   let config =
      PersistenceSupervisionConfig(
         isPersistableMessage,
         (fun msg confirmationId ->
            ConfirmableMessageEnvelope(confirmationId, "", msg)),
         minBackoff = opts.MinBackoff,
         maxBackoff = opts.MaxBackoff
      )

   Props.Create(fun () ->
      PersistenceSupervisor(
         childProps.ToProps(),
         persistenceId,
         config,
         strategy =
            SupervisorStrategy.StoppingStrategy.WithMaxNrOfRetries(
               opts.MaxNrOfRetries
            )
      ))

/// <summary>
/// Persist with ack sent to PersistenceSupervisor parent actor.
/// </summary>
let confirmPersist (ctx: Eventsourced<_>) (evt: obj) (confirmationId: int64) =
   evt
   |> Persist
   |> Effects.andThen (fun _ ->
      ctx.Parent() <! Confirmation(confirmationId, ctx.Pid))

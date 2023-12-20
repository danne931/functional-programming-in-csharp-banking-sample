module ActorUtil

open System
open Akkling
open Akkling.Cluster.Sharding
open Akka.Actor
open Akka.Cluster.Sharding
open Akka.Persistence.Query
open Akka.Persistence.Sql.Query
open Akka.Cluster.Tools.PublishSubscribe

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
            | :? ShardRegion.StartEntity as e -> e.EntityId
            | :? ShardEnvelope as e -> e.EntityId
            | :? Guid as id -> string id
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

   let roles = {|
      web = "web-role"
      account = "account-role"
      signalR = "signal-r-role"
      scheduling = "scheduling-role"
   |}

   let accountShardRegion = {
      name = "account"
      // TODO: Figure out ideal max #
      messageExtractor = messageExtractor 1000
   }

module ActorMetadata =
   type CircuitBreakerMarker() =
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

   type BillingCycleMarker() =
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
      Path: ActorPath option
      ProxyPath: ActorPath option
   }

   let private path route =
      ActorPath.Parse $"akka://bank/user/{route}"

   let account = {
      Name = "account"
      Path = Some <| path "account/account"
      ProxyPath = Some <| path "account-proxy"
   }

   let accountClosure = {
      Name = "account_closure"
      Path = Some <| path "account_closure/account_closure"
      ProxyPath = Some <| path "account_closure-proxy"
   }

   let internalTransfer = {
      Name = "internal_transfer_recipient"
      Path = None
      ProxyPath = None
   }

   let domesticTransfer = {
      Name = "domestic_transfer_recipient"
      Path =
         Some <| path "domestic_transfer_recipient/domestic_transfer_recipient"
      ProxyPath = Some <| path "domestic_transfer_recipient-proxy"
   }

   let billingCycle = {
      Name = "billing_cycle"
      Path = Some <| path "billing_cycle/billing_cycle"
      ProxyPath = Some <| path "billing_cycle-proxy"
   }

   let email = {
      Name = "email"
      Path = None
      ProxyPath = None
   }

   let auditor = {
      Name = "auditor"
      Path = None
      ProxyPath = None
   }

   let accountSeeder = {
      Name = "account-seeder"
      Path = None
      ProxyPath = None
   }

   let signalR = {
      Name = "signal-r"
      Path = None
      ProxyPath = None
   }

   let scheduling = {
      Name = "scheduling"
      Path = None
      ProxyPath = None
   }

   let circuitBreaker = {
      Name = "circuit-breaker"
      Path = None
      ProxyPath = None
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

   let shardId = shardRegionMeta.messageExtractor.ShardId entityId

   fac.RefFor shardId <| string entityId

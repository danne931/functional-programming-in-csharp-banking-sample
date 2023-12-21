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

   let internalTransfer = {
      Name = "internal-transfer-recipient"
      RouteBuilder =
         fun accountId -> $"account/{accountId}/internal-transfer-recipient"
   }

   let domesticTransfer = {
      Name = "domestic-transfer-recipient"
      Route = "domestic-transfer-recipient"
   }

   let billingCycle = {
      Name = "billing-cycle"
      Route = "billing-cycle"
   }

   let email = { Name = "email"; Route = "email" }

   let auditor = { Name = "auditor"; Route = "auditor" }

   let accountSeeder = {
      Name = "account-seeder"
      Route = "account-seeder"
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

   let shardId = shardRegionMeta.messageExtractor.ShardId entityId

   fac.RefFor shardId <| string entityId

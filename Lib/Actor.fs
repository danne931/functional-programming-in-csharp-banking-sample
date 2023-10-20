module ActorUtil

open System
open Akkling
open Akkling.Cluster.Sharding
open Akka.Actor
open Akka.Cluster.Sharding
open Akka.Persistence.Query
open Akka.Persistence.Sql.Query

type EntityRefGetter<'t> = Guid -> IEntityRef<'t>

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

module ActorMetadata =
   type EmailMarker() =
      class
      end

   type AccountClosureMarker() =
      class
      end

   type DomesticTransferMarker() =
      class
      end

   type BillingCycleBulkWriteMarker() =
      class
      end

   type AccountShardRegionMarker() =
      class
      end

   type ShardRegion = {
      name: string
      messageExtractor: IMessageExtractor
   }

   let accountShardRegion = {
      name = "account"
      // TODO: Figure out ideal max #
      messageExtractor = messageExtractor 1000
   }

   type ActorMetadata = { Name: string; Path: ActorPath option }

   let account = { Name = "account"; Path = None }

   let accountClosure = {
      Name = "account_closure"
      Path = Some(ActorPath.Parse "akka://bank/user/account_closure")
   }

   let internalTransfer = {
      Name = "internal_transfer_recipient"
      Path = None
   }

   let domesticTransfer = {
      Name = "domestic_transfer_recipient"
      Path = None
   }

   let billingCycle = { Name = "billing_cycle"; Path = None }

   let billingCycleBulkWrite = {
      Name = "billing_cycle_bulk_write"
      Path = None
   }

   let email = { Name = "email"; Path = None }

   let deadLettersMonitor = {
      Name = "dead_letters_monitor"
      Path = None
   }

let readJournal (system: ActorSystem) : SqlReadJournal =
   PersistenceQuery
      .Get(system)
      .ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier)

let getEntityRef
   system
   (shardRegionMeta: ActorMetadata.ShardRegion)
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

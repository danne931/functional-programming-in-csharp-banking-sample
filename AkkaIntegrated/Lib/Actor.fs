module ActorUtil

open System
open Akkling
open Akkling.Persistence
open Akkling.Cluster.Sharding
open Akka.Actor
open Akka.Cluster.Sharding
open Akka.Persistence.Query
open Akka.Persistence.MongoDb.Query

let getActorRef actorCtx path = task {
   try
      let! aref = (select actorCtx path).ResolveOne(TimeSpan.FromSeconds 3)
      return Some aref
   with ex when true ->
      printfn "ActorNotFoundException: %A %A" path ex
      return None
}

let getChildActorRef<'t, 'r>
   (actorCtx: Actor<'t>)
   (path: string)
   : IActorRef<'r> option
   =
   let accountRef = actorCtx.UntypedContext.Child(path)

   match accountRef = ActorRefs.Nobody with
   | true -> None
   | false -> Some(typed accountRef)

module ActorMetadata =
   type ActorMetadata = { Name: string; Path: string option }

   let account = { Name = "account"; Path = None }

   let internalTransfer = {
      Name = "internal_transfer_recipient"
      Path = None
   }

   let domesticTransfer = {
      Name = "domestic_transfer_recipient"
      Path = Some "akka://bank/user/domestic_transfer_recipient"
   }

   let maintenanceFee (id: Guid) = {
      Name = "maintenance_fee"
      Path = None
   }

   let deadLettersMonitor = {
      Name = "dead_letters_monitor"
      Path = None
   }

[<AbstractClass>]
type ClusteredActorFac(shardRegionName: string, system: ActorSystem) =
   abstract shardEnvelope: Guid -> _ -> ShardEnvelope

   member x.shardRegionRef() : IActorRef<_> =
      ClusterSharding.Get(system).ShardRegion(shardRegionName) |> typed

   member x.tell (entityId: Guid) (msg: _) =
      x.shardRegionRef () <! x.shardEnvelope entityId msg

   member x.ask<'t> (entityId: Guid) (msg: _) : Async<'t> =
      x.shardRegionRef () <? x.shardEnvelope entityId msg

type AccountActorFac(system: ActorSystem) =
   inherit ClusteredActorFac("account", system)

   override x.shardEnvelope (entityId: Guid) (msg: _) = {
      EntityId = string entityId
      ShardId = "shardid"
      Message = msg
   }

let readJournal (system: ActorSystem) : MongoDbReadJournal =
   PersistenceQuery
      .Get(system)
      .ReadJournalFor<MongoDbReadJournal>("akka.persistence.query.mongodb")
(*
type private MessageExtractor() =
   inherit HashCodeMessageExtractor(maxNumberOfShards = 1000)

   override x.EntityId(msg: obj) =
      match msg with
      | :? ShardRegion.StartEntity as e ->
         printfn "entityID: START ENTITY %A" e
         e.EntityId
      | :? ShardEnvelope as e ->
         printfn "entityID: ENVELOPE %A" e.EntityId
         e.EntityId
      | _ -> failwith "MessageExtractor.EntityId: Unknown message"

   override x.EntityMessage(msg: obj) = (msg :?> ShardEnvelope).Message
   override x.ShardId(msg: obj) =
      let hash = Akka.Util.MurmurHash.StringHash (x.EntityId msg)
      let r = Math.Abs(hash) % x.MaxNumberOfShards |> string
      printfn "--Shard id:-- %A" r
      r
*)
(*
let messageExtractor () =
   HashCodeMessageExtractor.Create(
      maxNumberOfShards = 1000,
      entityIdExtractor = (fun msg -> 
         match msg with
         | :? ShardRegion.StartEntity as e ->
            printfn "entityID: START ENTITY %A" e
            e.EntityId
         | :? ShardEnvelope as e ->
            printfn "entityID: ENVELOPE %A" e.EntityId
            e.EntityId
         | _ -> failwith "MessageExtractor.EntityId: Unknown message"),
      messageExtractor = fun msg -> (msg :?> ShardEnvelope).Message
   )
*)

type private MessageExtractor() =
   interface IMessageExtractor with
      member x.EntityId(msg: obj) = (msg :?> ShardEnvelope).EntityId

      member x.ShardId(msg: obj) =
         match msg with
         | :? ShardRegion.StartEntity as e ->
            printfn "shardID: START ENTITY %A" e
            "shardid"
         | :? ShardEnvelope as e ->
            printfn "shardID: ENVELOPE %A" e.ShardId
            "shardid"
         | _ -> failwith "MessageExtractor.ShardId: Unknown message"

      member x.EntityMessage(msg: obj) = (msg :?> ShardEnvelope).Message

// Adjusting Akkling's entityFactoryFor definition because it's MessageExtractor
// definition does not support listening for ShardRegion.StartEntity message
// which is necessary for remember-entities=true cluster setting.
// https://github.com/Horusiath/Akkling/blob/af62c8efadfc6a506e83fd119a57c17f7d8d14c9/src/Akkling.Cluster.Sharding/ClusterSharding.fs#L84
module AkklingExt =
   let private adjustPersistentProps
      (props: Props<'Message>)
      : Props<'Message>
      =
      if props.ActorType = typeof<FunPersistentActor<'Message>> then
         {
            props with
               ActorType = typeof<FunPersistentShardingActor<'Message>>
         }
      else
         props

   let entityFactoryFor
      (system: ActorSystem)
      (name: string)
      (props: Props<'Message>)
      : EntityFac<'Message>
      =
      let clusterSharding = ClusterSharding.Get(system)
      let adjustedProps = adjustPersistentProps props

      let shardRegion =
         clusterSharding.Start(
            name,
            adjustedProps.ToProps(),
            ClusterShardingSettings.Create(system),
            MessageExtractor()
         )

      {
         ShardRegion = shardRegion
         TypeName = name
      }

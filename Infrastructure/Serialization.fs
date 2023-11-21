namespace Bank.Infrastructure

open System
open System.Text.Json
open System.Runtime.Serialization
open Akka.Serialization
open Akka.Actor
open Akka.Persistence.Journal
open Akka.Cluster.Sharding
open Akkling.Cluster.Sharding

open Bank.Account.Domain
open BillingStatement
open Lib.Types

type AkkaPersistenceEventAdapter() =
   let envelopeFromJournal (entry: obj) : Envelope =
      let (Event evt) = unbox entry
      let _, envelope = AccountEnvelope.unwrap evt
      envelope

   interface IEventAdapter with
      member x.Manifest(evt: obj) = envelopeFromJournal(evt).EventName

      member x.ToJournal(evt: obj) : obj =
         let envelope = envelopeFromJournal evt
         Tagged(evt, Set.empty<string>.Add(envelope.EventName))

      member x.FromJournal(evt: obj, manifest: string) : IEventSequence =
         EventSequence.Single(evt)

type private AccountShardEnvelope = {
   EntityId: string
   ShardId: string
   Message: AccountMessage
}

type BankSerializer(system: ExtendedActorSystem) =
   inherit SerializerWithStringManifest(system)

   static member Name = "bank-serializer"

   override x.Identifier = 931

   override x.Manifest(o: obj) =
      match o with
      | :? SchedulingActor.Message -> "SchedulingActorMessage"
      | :? AccountClosureMessage -> "AccountClosureActorMessage"
      | :? Option<List<AccountEvent>> -> "AccountEventListOption"
      | :? AccountState -> "AccountState"
      | :? Option<AccountState> -> "AccountStateOption"
      | :? List<AccountState> -> "AccountStateList"
      | :? AccountMessage as msg ->
         match msg with
         | AccountMessage.Event _ -> "AccountEvent"
         | _ -> "AccountMessage"
      | :? SignalRMessage -> "SignalRMessage"
      | :? BillingMessage -> "BillingCycleBulkWriteActorMessage"
      | :? EmailActor.EmailMessage -> "EmailActorMessage"
      | :? ShardEnvelope as e ->
         match e.Message with
         | :? AccountMessage -> "AccountShardEnvelope"
         | _ -> raise <| NotImplementedException()
      | _ -> raise <| NotImplementedException()

   override x.ToBinary(o: obj) =
      match o with
      // SchedulingActor message serialization for Quartz job persistence.
      | :? SchedulingActor.Message
      // Message serialization for messages from sharded account nodes to
      // Email cluster singleton.
      | :? EmailActor.EmailMessage
      // Message serialization for messages from sharded account nodes to
      // BillingCycleBulkWrite cluster singleton.
      // Also for messages from SchedulingActor to Billing Cycle Proxy
      | :? BillingMessage
      // Serialization for messages from sharded account nodes to
      // AccountClosureActor cluster singleton. Also for messages
      // from SchedulingActor to Account Closure Proxy
      | :? AccountClosureMessage as msg ->
         JsonSerializer.SerializeToUtf8Bytes(msg, Serialization.jsonOptions)
      // Akka ShardRegionProxy defined in Akka.Hosting does not
      // recognize Akkling ShardEnvelope as Akka ShardingEnvelope
      // so need to explicitly add it for message extraction.
      //
      // This would be unnecessary if I was using Akka.Hosting
      // IRequiredActor<> to dependency inject the account shard
      // region proxy.  However, doing so would lose Akkling's typed
      // actor message benefits when forwarding a message from
      // Akka ShardRegionProxy.
      | :? ShardEnvelope as e ->
         match e.Message with
         | :? AccountMessage ->
            JsonSerializer.SerializeToUtf8Bytes(e, Serialization.jsonOptions)
         | _ -> raise <| NotImplementedException()
      // AccountMessage.GetEvents response serialized for message sent
      // from account cluster nodes to Web node.
      | :? Option<List<AccountEvent>>
      // AccountMessage.GetAccount response serialized for message sent
      // from account cluster nodes to Web node.
      | :? Option<AccountState>
      // AccountClosureActor persistence snapshot.
      | :? List<AccountState>
      // Serialization for message sent from SignalRProxy on account cluster
      // nodes to SignalRActor on Web node.
      | :? SignalRMessage
      // AccountActor persistence snapshot.
      | :? AccountState as o ->
         JsonSerializer.SerializeToUtf8Bytes(o, Serialization.jsonOptions)
      | :? AccountMessage as msg ->
         match msg with
         // AccountEvent actor message replay
         | AccountMessage.Event e ->
            JsonSerializer.SerializeToUtf8Bytes(e, Serialization.jsonOptions)
         | msg ->
            JsonSerializer.SerializeToUtf8Bytes(msg, Serialization.jsonOptions)
      | _ -> raise <| NotImplementedException()

   (*
      NOTE:
      https://getakka.net/articles/serialization/serialization.html

      It's recommended to throw SerializationException in FromBinary(Byte[], String)
      if the manifest is unknown.  This makes it possible to introduce new message
      types and send them to nodes that don't know about them. This is typically
      needed when performing rolling upgrades, i.e. running a cluster with mixed
      versions for while. SerializationException is treated as a transient problem
      in the TCP based remoting layer.  The problem will be logged and message is
      dropped.  Other exceptions will tear down the TCP connection because it can
      be an indication of corrupt bytes from the underlying transport.
   *)
   override x.FromBinary(bytes: byte[], manifest: string) : obj =
      let deserializeToType =
         match manifest with
         | "AccountState" -> typeof<AccountState>
         | "AccountStateOption" -> typeof<AccountState option>
         | "AccountStateList" -> typeof<AccountState list>
         | "AccountEvent" -> typeof<AccountEvent>
         | "AccountEventListOption" -> typeof<AccountEvent list option>
         | "AccountMessage" -> typeof<AccountMessage>
         | "AccountShardEnvelope" -> typeof<AccountShardEnvelope>
         | "SignalRMessage" -> typeof<SignalRMessage>
         | "BillingCycleBulkWriteActorMessage" -> typeof<BillingMessage>
         | "AccountClosureActorMessage" -> typeof<AccountClosureMessage>
         | "EmailActorMessage" -> typeof<EmailActor.EmailMessage>
         | "SchedulingActorMessage" -> typeof<SchedulingActor.Message>
         | _ -> raise <| SerializationException()

      let deserialized =
         JsonSerializer.Deserialize(
            bytes,
            deserializeToType,
            Serialization.jsonOptions
         )

      match deserialized with
      | :? AccountShardEnvelope as e ->
         // NOTE:
         // Deserialize as AccountShardEnvelope instead
         // of ShardEnvelope so envelope.Message
         // is AccountMessage type rather than JObject.
         // Return a ShardEnvelope type so the message
         // gets routed correctly.
         let envelope: ShardEnvelope = {
            EntityId = e.EntityId
            ShardId = e.ShardId
            Message = e.Message
         }

         envelope
      | _ -> deserialized

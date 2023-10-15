[<RequireQualifiedAccess>]
module Serialization

open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Runtime.Serialization
open Akka.Serialization
open Akka.Actor

open BankTypes
open Lib.Types

let private baseConfig =
   JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags()

let mergeDefaultJsonOptions (options: JsonSerializerOptions) =
   options.Converters.Add(JsonStringEnumConverter())
   options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase

let jsonOptions = baseConfig.ToJsonSerializerOptions()
mergeDefaultJsonOptions jsonOptions

let withInjectedOptions opts =
   mergeDefaultJsonOptions opts
   baseConfig.AddToJsonSerializerOptions opts

let serialize (data: _) : string =
   JsonSerializer.Serialize(data, jsonOptions)

let deserialize<'t> (data: string) : Result<'t, string> =
   try
      JsonSerializer.Deserialize<'t>(data) |> Ok
   with err ->
      $"Deserialization error: {err.Message}" |> Error

let private envelopeFromJournal (entry: obj) : Envelope =
   let (Event evt) = unbox entry
   let _, envelope = Envelope.unwrap evt
   envelope

open Akka.Persistence.Journal

type AkkaPersistenceEventAdapter() =
   interface IEventAdapter with
      member x.Manifest(evt: obj) = envelopeFromJournal(evt).EventName

      member x.ToJournal(evt: obj) : obj =
         let envelope = envelopeFromJournal evt
         Tagged(evt, Set.empty<string>.Add(envelope.EventName))

      member x.FromJournal(evt: obj, manifest: string) : IEventSequence =
         EventSequence.Single(evt)

type AkkaSerializer(system: ExtendedActorSystem) =
   inherit SerializerWithStringManifest(system)

   override x.Identifier = 931

   override x.Manifest(o: obj) =
      match o with
      | :? AccountState -> "AccountState"
      | :? AccountMessage as msg ->
         match msg with
         | AccountMessage.Event _ -> "AccountEvent"
         | _ -> raise <| NotImplementedException()
      | _ -> raise <| NotImplementedException()

   override x.ToBinary(o: obj) =
      match o with
      | :? AccountState as account ->
         JsonSerializer.SerializeToUtf8Bytes(account, jsonOptions)
      | :? AccountMessage as msg ->
         match msg with
         | AccountMessage.Event e ->
            JsonSerializer.SerializeToUtf8Bytes(e, jsonOptions)
         | _ -> raise <| NotImplementedException()
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
         | "AccountEvent" -> typeof<AccountEvent>
         | _ -> raise <| SerializationException()

      JsonSerializer.Deserialize(bytes, deserializeToType, jsonOptions)

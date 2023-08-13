[<RequireQualifiedAccess>]
module Serialization

open System.Text.Json
open System.Text.Json.Serialization
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
   with err when true ->
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

type AccountEventSerializer(system: ExtendedActorSystem) =
   inherit Serializer(system)

   override x.Identifier = 931
   override x.IncludeManifest = false

   override x.ToBinary(o: obj) =
      match unbox o with
      | AccountMessage.Event e ->
         JsonSerializer.SerializeToUtf8Bytes(e, jsonOptions)
      | _ -> failwith "Attempt to serialize unknown object"

   override x.FromBinary(bytes: byte[], _) : obj =
      JsonSerializer.Deserialize(bytes, typeof<AccountEvent>, jsonOptions)

type AccountSnapshotSerializer(system: ExtendedActorSystem) =
   inherit Serializer(system)

   override x.Identifier = 932
   override x.IncludeManifest = false

   override x.ToBinary(o: obj) =
      match o with
      | :? AccountState as account ->
         JsonSerializer.SerializeToUtf8Bytes(account, jsonOptions)
      | _ -> failwith "Attempt to serialize unknown object"

   override x.FromBinary(bytes: byte[], _) : obj =
      JsonSerializer.Deserialize(bytes, typeof<AccountState>, jsonOptions)

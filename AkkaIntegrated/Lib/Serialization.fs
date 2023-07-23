[<RequireQualifiedAccess>]
module Serialization

open System.Text.Json
open System.Text.Json.Serialization

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
         Tagged(evt, Set.empty<string>.Add("Account").Add(envelope.EventName))

      member x.FromJournal(evt: obj, manifest: string) : IEventSequence =
         EventSequence.Single(evt)

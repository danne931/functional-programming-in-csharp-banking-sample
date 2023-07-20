[<RequireQualifiedAccessAttribute>]
module EventStoreManager

open EventStore.Client
open Microsoft.FSharp.Core.Option
open FSharp.Control
open FSharp.Control.TaskSeq

open Lib.Types
open BankTypes

let tryDeserialize (e: ResolvedEvent) =
   try
      Serialization.deserializeEvent (e.Event.Data.ToArray()) e.Event.EventType
   with ex when true ->
      printfn "tryDeserialize fail: %A" ex.Message
      reraise ()

let readStream (es: EventStoreClient) streamName (resolveLinkTos: bool) = task {
   let stream =
      es.ReadStreamAsync(
         Direction.Forwards,
         streamName,
         StreamPosition.Start,
         resolveLinkTos = resolveLinkTos
      )

   let! readState = stream.ReadState

   if (readState = ReadState.StreamNotFound) then
      return None
   else
      let! events = stream |> map tryDeserialize |> toListAsync
      return Some events
}

let save
   (es: EventStoreClient)
   streamName
   ((event, envelope): OpenEventEnvelope)
   (expectedStreamState: StreamState option)
   =
   let streamState =
      expectedStreamState |> defaultValue StreamState.StreamExists

   let esEvent =
      EventData(
         Uuid.NewUuid(),
         envelope.EventName,
         Serialization.serializeEvent event
      )

   es.AppendToStreamAsync(streamName, streamState, [ esEvent ]) |> Task.ignore

let softDelete (es: EventStoreClient) streamName =
   es.DeleteAsync(streamName, StreamState.Any)

let exists (es: EventStoreClient) streamName = task {
   let stream =
      es.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start)

   let! readState = stream.ReadState
   return readState <> ReadState.StreamNotFound
}

let connect connString =
   EventStoreClientSettings.Create connString |> EventStoreClient

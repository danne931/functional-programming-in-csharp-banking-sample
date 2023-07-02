using EventStore.Client;
using System.Text.Json;
using LanguageExt;
using static LanguageExt.Prelude;

using Lib.Types;

namespace Lib.Persistence;

public static class EventStoreManager {
   public async static Task<Unit> Save(
      EventStoreClient es,
      Map<string, Type> mapping,
      string streamName,
      Event evt,
      StreamState? expectedStreamState = null
   ) {
      var typeFromName = mapping[evt.Name];

      await es.AppendToStreamAsync(
         streamName,
         expectedStreamState ?? StreamState.StreamExists,
         new[] {
            new EventData(
               eventId: Uuid.NewUuid(),
               type: evt.Name,
               data: JsonSerializer.SerializeToUtf8Bytes(evt, typeFromName)
            )
         }
      );

      return unit;
   }

   public static async Task<Option<Lst<object>>> ReadStream(
      EventStoreClient es,
      string streamName,
      Map<string, Type> mapping,
      bool resolveLinkTos = false
   ) {
      var stream = es.ReadStreamAsync(
         Direction.Forwards,
         streamName,
         StreamPosition.Start,
         resolveLinkTos: resolveLinkTos
      );
      if (await stream.ReadState == ReadState.StreamNotFound)
         return None;

      return await stream.AggregateAsync(
         List<object>(),
         (acc, e) =>
            acc.Add(JsonSerializer.Deserialize(
               e.Event.Data.ToArray(),
               mapping[e.Event.EventType]
            )!)
         );
   }

   public async static Task<Unit> SoftDelete(
      EventStoreClient es,
      string streamName
   ) {
      await es.DeleteAsync(streamName, StreamState.Any);
      return unit;
   }

   public static async Task<bool> Exists(
      EventStoreClient es,
      string streamName
   ) {
      var stream = es.ReadStreamAsync(
         Direction.Forwards,
         streamName,
         StreamPosition.Start
      );
      return (await stream.ReadState != ReadState.StreamNotFound);
   }

   public static EventStoreClient Connect(string connString) =>
      new EventStoreClient(EventStoreClientSettings.Create(connString));
}
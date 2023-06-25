[<RequireQualifiedAccess>]
module Config

open System
open Akka.Event
open Akkling
open EventStore.Client
open Microsoft.AspNetCore.SignalR
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

open Bank.Account.Api
open Bank.Hubs
open type AccountActor.AccountRegistry

let enableDefaultHttpJsonSerialization (builder: WebApplicationBuilder) =
   builder.Services.ConfigureHttpJsonOptions(fun opts ->
      Serialization.mergeDefaultJsonOptions opts.SerializerOptions
      ())
   |> ignore

let startActorModel () =
   let system = System.create "bank" (Configuration.defaultConfig ())

   let deadLetterHandler (msg: AllDeadLetters) =
      printfn
         "Dead Letters:\nFrom: %A\nTo: %A\nMessage: %A"
         msg.Sender.Path
         msg.Recipient.Path
         msg.Message

      Ignore

   let deadLetterRef =
      spawn system "deadletters" (props (actorOf deadLetterHandler))

   EventStreaming.subscribe deadLetterRef system.EventStream |> ignore

   system


let startSignalR (builder: WebApplicationBuilder) =
   builder.Services
      .AddSignalR()
      .AddJsonProtocol(fun opts ->
         Serialization.withInjectedOptions opts.PayloadSerializerOptions)
   |> ignore

let startEventStore (builder: WebApplicationBuilder) =
   let connString =
      builder.Configuration.GetSection("ConnectionStrings").Item "EventStore"

   if isNull connString then
      failwith "EventStore connection string not found in appsettings"

   EventStoreManager.connect connString

let injectDependencies
   (builder: WebApplicationBuilder)
   (esClient: EventStoreClient)
   (actorSystem: Akka.Actor.ActorSystem)
   =
   builder.Services.AddSingleton<AccountActor.AccountRegistry>(fun provider -> {
      system = actorSystem
      loadAccountEvents = (getAccountEvents esClient)
      loadAccount = getAccount (getAccountEvents esClient)
      saveAndPublish = saveAndPublish esClient
      broadcast =
         (fun (event, accountState) ->
            provider
               .GetRequiredService<IHubContext<AccountHub, IAccountClient>>()
               .Clients.Group(accountState.EntityId.ToString())
               .ReceiveMessage(
                  {|
                     event = event
                     newState = accountState
                  |}
               ))
      broadcastError =
         (fun errMsg ->
            provider
               .GetRequiredService<IHubContext<AccountHub, IAccountClient>>()
               .Clients.All.ReceiveError(errMsg))
   })
   |> ignore

   builder.Services.AddSingleton<EventStoreClient>(esClient) |> ignore
   ()

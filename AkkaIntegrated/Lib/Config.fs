[<RequireQualifiedAccess>]
module Config

open System
open Akka.Actor
open Akka.Event
open Akka.Pattern
open Akkling
open Microsoft.AspNetCore.SignalR
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

open BankTypes
open Bank.Hubs
open ActorUtil

let enableDefaultHttpJsonSerialization (builder: WebApplicationBuilder) =
   builder.Services.ConfigureHttpJsonOptions(fun opts ->
      Serialization.withInjectedOptions opts.SerializerOptions
      ())
   |> ignore

let startActorModel () =
   let system = System.create "bank" <| Configuration.load ()

   let deadLetterHandler (msg: AllDeadLetters) =
      printfn "Dead letters: %A" msg
      Ignore

   let deadLetterRef =
      spawn
         system
         ActorMetadata.deadLettersMonitor.Name
         (props (actorOf deadLetterHandler))

   EventStreaming.subscribe deadLetterRef system.EventStream |> ignore

   system

let startSignalR (builder: WebApplicationBuilder) =
   builder.Services
      .AddSignalR()
      .AddJsonProtocol(fun opts ->
         Serialization.withInjectedOptions opts.PayloadSerializerOptions)
   |> ignore

let injectDependencies
   (builder: WebApplicationBuilder)
   (actorSystem: ActorSystem)
   =
   let pgConnString =
      builder.Configuration.GetSection("ConnectionStrings").Item "Postgres"

   if isNull pgConnString then
      failwith "Missing Postgres connection string in appsettings.json"

   Environment.SetEnvironmentVariable("PostgresConnectionString", pgConnString)

   let initBroadcast (provider: IServiceProvider) : AccountBroadcast = {
      broadcast =
         (fun (event, accountState) ->
            provider
               .GetRequiredService<IHubContext<AccountHub, IAccountClient>>()
               .Clients.Group(string accountState.EntityId)
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
      broadcastCircuitBreaker =
         (fun circuitBreakerMessage ->
            provider
               .GetRequiredService<IHubContext<AccountHub, IAccountClient>>()
               .Clients.All.ReceiveCircuitBreakerMessage(circuitBreakerMessage))
   }

   builder.Services.AddSingleton<AccountBroadcast>(initBroadcast) |> ignore

   builder.Services.AddSingleton<AccountActorFac>(fun provider ->
      let broadcast = provider.GetRequiredService<AccountBroadcast>()
      AccountActor.start broadcast actorSystem |> ignore
      ActorUtil.AccountActorFac actorSystem)
   |> ignore

   builder.Services.AddSingleton<IActorRef<DomesticTransferRecipientActor.Message>>
      (fun provider ->
         DomesticTransferRecipientActor.start actorSystem
         <| CircuitBreaker(
            actorSystem.Scheduler,
            maxFailures = 2,
            callTimeout = TimeSpan.FromSeconds 7,
            resetTimeout = TimeSpan.FromMinutes 1
         )
         <| provider.GetRequiredService<AccountBroadcast>()
         <| provider.GetRequiredService<AccountActorFac>())
   |> ignore

   builder.Services.AddSingleton<ActorSystem>(actorSystem) |> ignore

   ()

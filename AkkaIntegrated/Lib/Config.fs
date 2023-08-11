[<RequireQualifiedAccess>]
module Config

open System
open Akka.Event
open Akka.Pattern
open Akkling
open Akka.Cluster.Sharding
open Microsoft.AspNetCore.SignalR
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Quartz
open Akka.Quartz.Actor

open BankTypes
open Bank.Hubs
open Bank.Account.Api
open ActorUtil

let enableDefaultHttpJsonSerialization (builder: WebApplicationBuilder) =
   builder.Services.ConfigureHttpJsonOptions(fun opts ->
      Serialization.withInjectedOptions opts.SerializerOptions
      ())
   |> ignore

let startSignalR (builder: WebApplicationBuilder) =
   builder.Services
      .AddSignalR()
      .AddJsonProtocol(fun opts ->
         Serialization.withInjectedOptions opts.PayloadSerializerOptions)
   |> ignore

// TODO: Integrate Akka.Hosting HOCON-less configuration,
//       actor instantiation & dependency injection
let startActorModel (builder: WebApplicationBuilder) =
   builder.Services.AddHostedService<AkkaService>(fun sp ->
      sp.GetRequiredService<AkkaService>())
   |> ignore

let injectDependencies (builder: WebApplicationBuilder) =
   builder.Services.AddSingleton<AkkaService>() |> ignore

   let quartzSchedulerFactory =
      builder.Services
         .BuildServiceProvider()
         .GetRequiredService<ISchedulerFactory>()

   builder.Services.AddSingleton<IScheduler>(
      quartzSchedulerFactory.GetScheduler().Result
   )
   |> ignore

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
      broadcastBillingCycleEnd =
         (fun _ ->
            provider
               .GetRequiredService<IHubContext<AccountHub, IAccountClient>>()
               .Clients.All.ReceiveBillingCycleEnd())
   }

   builder.Services.AddSingleton<AccountBroadcast>(initBroadcast) |> ignore

   builder.Services.AddSingleton<AccountActorFac>(fun provider ->
      let akkaService = provider.GetRequiredService<AkkaService>()
      let system = (akkaService :> IBridge).getActorSystem ()

      let deadLetterHandler (msg: AllDeadLetters) =
         printfn "Dead letters: %A" msg
         Ignore

      let deadLetterRef =
         spawn
            system
            ActorMetadata.deadLettersMonitor.Name
            (props (actorOf deadLetterHandler))

      EventStreaming.subscribe deadLetterRef system.EventStream |> ignore

      let scheduler = provider.GetRequiredService<IScheduler>()

      let quartzPersistentARef =
         system.ActorOf(
            Akka.Actor.Props.Create(fun () -> QuartzPersistentActor scheduler),
            "QuartzScheduler"
         )

      EmailActor.start system |> ignore
      BillingCycleActor.scheduleMonthly system quartzPersistentARef
      AccountClosureActor.start system quartzPersistentARef |> ignore
      AccountClosureActor.scheduleNightlyCheck quartzPersistentARef

      let broadcast = provider.GetRequiredService<AccountBroadcast>()
      let persistence = { getEvents = getAccountEvents system }

      AccountActor.start persistence broadcast system |> ignore
      ActorUtil.AccountActorFac system)
   |> ignore

   builder.Services.AddSingleton<IActorRef<DomesticTransferRecipientActor.Message>>
      (fun provider ->
         let akkaService = provider.GetRequiredService<AkkaService>()
         let actorSystem = (akkaService :> IBridge).getActorSystem ()

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

   ()

let startQuartz (builder: WebApplicationBuilder) =
   builder.Services
      .AddOptions<QuartzOptions>()
      .Configure(fun opts ->
         opts.SchedulerName <- "Quartz Bank Scheduler"
         opts.Scheduling.OverWriteExistingData <- false
         // Attempts to add a job with a name of a job already
         // scheduled will be ignored.
         opts.Scheduling.IgnoreDuplicates <- true
         ())
      .Services.AddQuartz(fun q ->
         q.UsePersistentStore(fun store ->
            store.SetProperty("quartz.jobStore.tablePrefix", "qrtz_")

            store.UsePostgres(
               Environment.GetEnvironmentVariable(
                  "PostgresConnectionStringAdoFormat"
               )
            )

            store.UseNewtonsoftJsonSerializer()

            store.PerformSchemaValidation <- true

            store.SetProperty(
               "quartz.plugin.shutdownhook.type",
               "Quartz.Plugin.Management.ShutdownHookPlugin, Quartz.Plugins"
            )

            store.SetProperty(
               "quartz.plugin.shutdownhook.cleanShutdown",
               "true"
            )

            store.SetProperty(
               "quartz.plugin.triggHistory.type",
               "Quartz.Plugin.History.LoggingTriggerHistoryPlugin, Quartz.Plugins"
            )

            store.SetProperty(
               "quartz.plugin.triggHistory.triggerFiredMessage",
               "Trigger {1}.{0} fired job {6}.{5} at: {4:HH:mm:ss MM/dd/yyyy}"
            )

            store.SetProperty(
               "quartz.plugin.triggHistory.triggerCompleteMessage",
               "Trigger {1}.{0} completed firing job {6}.{5} at {4:HH:mm:ss MM/dd/yyyy} with resulting trigger instruction code: {9}"
            )))
   |> ignore

   builder.Services.AddQuartzHostedService(fun opts ->
      // Interrupt shutdown & wait for executing jobs to finish first.
      opts.WaitForJobsToComplete <- true
      // Avoid running jobs until application started
      opts.AwaitApplicationStarted <- true)
   |> ignore

let setEnvironment (builder: WebApplicationBuilder) =
   let pgConnString =
      builder.Configuration.GetSection("ConnectionStrings").Item "Postgres"

   if isNull pgConnString then
      failwith "Missing Postgres connection string in appsettings.json"

   Environment.SetEnvironmentVariable("PostgresConnectionString", pgConnString)

   let pgAdoConnString =
      builder.Configuration.GetSection("ConnectionStrings").Item
         "PostgresAdoFormat"

   if isNull pgAdoConnString then
      failwith "Missing Postgres ADO connection string in appsettings.json"

   Environment.SetEnvironmentVariable(
      "PostgresConnectionStringAdoFormat",
      pgAdoConnString
   )

[<RequireQualifiedAccess>]
module Config

open System
open Microsoft.AspNetCore.SignalR
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Serilog
open Serilog.Sinks.SystemConsole
open Serilog.Formatting.Compact
open Akka.Logger.Serilog
open Akka.Actor
open Akka.Event
open Akka.Routing
open Akka.Pattern
open Akka.Hosting
open Akka.Remote.Hosting
open Akka.Cluster.Hosting
open Akka.Persistence.Hosting
open Akka.Persistence.Sql
open Akka.Persistence.Sql.Hosting
open Akka.Cluster.Sharding
open Akka.Quartz.Actor
open Quartz
open Akkling

open BankTypes
open Bank.Hubs
open Bank.Account.Api
open ActorUtil

let private envConfig = EnvironmentConfig.config

let startLogger (builder: WebApplicationBuilder) =
   // NOTE: Initial logger logs errors during during start up.
   //       It's replaced by the config in UseSerilog below.
   Log.Logger <- LoggerConfiguration().WriteTo.Console().CreateLogger()

   builder.Host.UseSerilog(fun ctx services loggerConfig ->
      loggerConfig.ReadFrom
         .Configuration(ctx.Configuration)
         .ReadFrom.Services(services)
         .Enrich.FromLogContext()
         .WriteTo.Console(theme = Themes.AnsiConsoleTheme.Code)
         .WriteTo.File(CompactJsonFormatter(), envConfig.SerilogOutputFile)
      |> ignore)
   |> ignore

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

let startActorModel (builder: WebApplicationBuilder) =
   let connString = envConfig.ConnectionStrings.PostgresAdoFormat
   let dbProvider = envConfig.AkkaPersistence.DbProvider

   let journalOpts = SqlJournalOptions()
   journalOpts.ConnectionString <- connString
   journalOpts.ProviderName <- dbProvider
   journalOpts.AutoInitialize <- true
   let jdo = JournalDatabaseOptions(DatabaseMapping.Default)
   let jto = JournalTableOptions()
   jto.TableName <- envConfig.AkkaPersistence.JournalTableName
   jto.UseWriterUuidColumn <- true
   jdo.JournalTable <- jto
   journalOpts.DatabaseOptions <- jdo

   journalOpts.Adapters.AddEventAdapter<Serialization.AkkaPersistenceEventAdapter>(
      "v1",
      [ typedefof<BankTypes.AccountMessage> ]
   )
   |> ignore

   let snapshotOpts = SqlSnapshotOptions()
   snapshotOpts.ConnectionString <- connString
   snapshotOpts.ProviderName <- dbProvider
   snapshotOpts.AutoInitialize <- true
   let sdo = SnapshotDatabaseOptions(DatabaseMapping.Default)
   let sto = SnapshotTableOptions()
   sto.TableName <- envConfig.AkkaPersistence.SnapshotTableName
   sdo.SnapshotTable <- sto
   snapshotOpts.DatabaseOptions <- sdo

   let akkaConfig = builder.Configuration.GetSection "akka"

   builder.Services.AddAkka(
      envConfig.AkkaSystemName,
      (fun builder provider ->
         builder
            .AddHocon(akkaConfig, HoconAddMode.Prepend)
            .AddHocon(
               """
                  billing-cycle-bulk-write-mailbox: {
                     mailbox-type: "BillingCycleBulkWriteActor+PriorityMailbox, AkkaIntegrated"
                  }
               """,
               HoconAddMode.Prepend
            )
            .WithRemoting(envConfig.AkkaRemoteHost, envConfig.AkkaRemotePort)
            .WithClustering(
               ClusterOptions(
                  SeedNodes = List.toArray envConfig.AkkaClusterSeedNodes
               )
            )
            .WithSqlPersistence(connString, dbProvider, PersistenceMode.Both)
            .WithJournalAndSnapshot(journalOpts, snapshotOpts)
            .WithCustomSerializer(
               "json",
               [ typedefof<String>; typedefof<Object> ],
               fun system ->
                  Akka.Serialization.NewtonSoftJsonSerializer(system)
            )
            .WithCustomSerializer(
               "accountevent",
               [ typedefof<BankTypes.AccountMessage> ],
               fun system -> Serialization.AccountEventSerializer(system)
            )
            .WithCustomSerializer(
               "accountsnapshot",
               [ typedefof<BankTypes.AccountState> ],
               fun system -> Serialization.AccountSnapshotSerializer(system)
            )
            // TODO: See if can init Akkling typed clustered account actor
            //       here and do away with ActorUtil.AkklingExt.entityFactoryFor
            //       & sharding HOCON config.  May have to forgo Akkling typed
            //       actor ref if done this way.
            //.WithShardRegion("account", )
            .ConfigureLoggers(fun builder ->
               builder.LogLevel <- LogLevel.InfoLevel
               //builder.LogConfigOnStart <- true
               builder.AddLogger<SerilogLogger>() |> ignore

               builder.LogMessageFormatter <-
                  typeof<SerilogLogMessageFormatter>)
            .WithActors(fun system registry ->
               let broadcast = provider.GetRequiredService<AccountBroadcast>()

               let persistence =
                  provider.GetRequiredService<AccountPersistence>()

               let deadLetterHandler (ctx: Actor<_>) (msg: AllDeadLetters) =
                  logError ctx $"Dead letters: {msg}"
                  Ignore

               let deadLetterRef =
                  spawn
                     system
                     ActorMetadata.deadLettersMonitor.Name
                     (props (actorOf2 deadLetterHandler))

               EventStreaming.subscribe deadLetterRef system.EventStream
               |> ignore

               let scheduler = provider.GetRequiredService<IScheduler>()

               let quartzPersistentARef =
                  system.ActorOf(
                     Akka.Actor.Props.Create(fun () ->
                        QuartzPersistentActor scheduler),
                     "QuartzScheduler"
                  )

               registry.Register<QuartzPersistentActor>(quartzPersistentARef)

               BillingCycleActor.scheduleMonthly system quartzPersistentARef

               registry.Register<ActorMetadata.BillingCycleBulkWriteMarker>(
                  BillingCycleBulkWriteActor.start system |> untyped
               )

               registry.Register<ActorMetadata.AccountClosureMarker>(
                  AccountClosureActor.start system quartzPersistentARef
                  |> untyped
               )

               AccountClosureActor.scheduleNightlyCheck quartzPersistentARef

               registry.Register<ActorMetadata.EmailMarker>(
                  EmailActor.start system
                  <| CircuitBreaker(
                     system.Scheduler,
                     maxFailures = 2,
                     callTimeout = TimeSpan.FromSeconds 7,
                     resetTimeout = TimeSpan.FromMinutes 1
                  )
                  <| broadcast
                  |> untyped
               )

               AccountActor.start persistence broadcast system |> ignore

               let fac = provider.GetRequiredService<AccountActorFac>()

               let domesticTransferRouter =
                  RoundRobinPool(1, DefaultResizer(1, 10))

               registry.Register<ActorMetadata.DomesticTransferMarker>(
                  DomesticTransferRecipientActor.start system
                  <| CircuitBreaker(
                     system.Scheduler,
                     maxFailures = 2,
                     callTimeout = TimeSpan.FromSeconds 7,
                     resetTimeout = TimeSpan.FromMinutes 1
                  )
                  <| provider.GetRequiredService<AccountBroadcast>()
                  <| fac
                  <| domesticTransferRouter
                  |> untyped
               )

               ())
         |> ignore

         ())
   )
   |> ignore

   ()

let injectDependencies (builder: WebApplicationBuilder) =
   builder.Services.AddSingleton<IScheduler>(
      builder.Services
         .BuildServiceProvider()
         .GetRequiredService<ISchedulerFactory>()
         .GetScheduler()
         .Result
   )
   |> ignore

   builder.Services.AddSingleton<AccountBroadcast>(fun provider -> {
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
               .Clients.All.ReceiveCircuitBreakerMessage(
                  circuitBreakerMessage
               ))
      broadcastBillingCycleEnd =
         (fun _ ->
            provider
               .GetRequiredService<IHubContext<AccountHub, IAccountClient>>()
               .Clients.All.ReceiveBillingCycleEnd())
   })
   |> ignore

   builder.Services.AddSingleton<AccountPersistence>(fun provider ->
      let system = provider.GetRequiredService<ActorSystem>()
      { getEvents = getAccountEvents system })
   |> ignore

   builder.Services.AddSingleton<AccountActorFac>(fun provider ->
      let system = provider.GetRequiredService<ActorSystem>()
      ActorUtil.AccountActorFac system)
   |> ignore

   ()

let startQuartz (builder: WebApplicationBuilder) =
   builder.Services
      .AddOptions<QuartzOptions>()
      .Configure(fun opts ->
         opts.SchedulerName <- envConfig.Quartz.SchedulerName
         opts.Scheduling.OverWriteExistingData <- false
         // Attempts to add a job with a name of a job already
         // scheduled will be ignored.
         opts.Scheduling.IgnoreDuplicates <- true
         ())
      .Services.AddQuartz(fun q ->
         q.UsePersistentStore(fun store ->
            store.SetProperty(
               "quartz.jobStore.tablePrefix",
               envConfig.Quartz.TablePrefix
            )

            store.UsePostgres(envConfig.ConnectionStrings.PostgresAdoFormat)

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

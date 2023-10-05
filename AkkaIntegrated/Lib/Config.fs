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
open Akka.Hosting
open Akka.Remote.Hosting
open Akka.Cluster.Hosting
open Akka.Persistence.Hosting
open Akka.Persistence.Sql.Hosting
open Akka.Quartz.Actor
open Petabridge.Cmd.Host
open Petabridge.Cmd.Cluster
open Petabridge.Cmd.Cluster.Sharding
open Quartz
open Akkling

open BankTypes
open Lib.Types
open Bank.Hubs
open Bank.Account.Api
open Bank.Account.Domain
open Bank.BillingCycle.Api
open ActorUtil

let private envConfig = EnvironmentConfig.config

// Create accounts for local development
let private seed (sys: ActorSystem) = task {
   let commands = [
      CreateAccountCommand(
         entityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4"),
         firstName = "Jelly",
         lastName = "Fish",
         balance = 1300,
         email = "jellyfish@gmail.com",
         currency = Currency.USD,
         correlationId = Guid.NewGuid()
      )

      CreateAccountCommand(
         entityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5"),
         firstName = "Star",
         lastName = "Fish",
         balance = 1000,
         email = "starfish@gmail.com",
         currency = Currency.USD,
         correlationId = Guid.NewGuid()
      )
   ]

   for command in commands do
      let ref = AkklingExt.getEntityRef sys "account" command.EntityId
      let! (acct: AccountState option) = ref <? AccountMessage.Lookup

      if acct.IsNone then
         sys.Log.Log(
            Akka.Event.LogLevel.InfoLevel,
            null,
            $"Account doesn't exist.  Will create for {command.Email}"
         )

         ref <! AccountMessage.StateChange command
}

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
            .AddPetabridgeCmd(fun cmd ->
               cmd.RegisterCommandPalette(ClusterCommands.Instance) |> ignore

               cmd.RegisterCommandPalette(ClusterShardingCommands.Instance)
               |> ignore)
            .ConfigureLoggers(fun builder ->
               builder.LogLevel <- LogLevel.InfoLevel
               //builder.LogConfigOnStart <- true
               builder.AddLogger<SerilogLogger>() |> ignore

               builder.LogMessageFormatter <-
                  typeof<SerilogLogMessageFormatter>)
            .WithActors(fun system registry ->
               let broadcast = provider.GetRequiredService<AccountBroadcast>()

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

               let accountFac =
                  provider.GetRequiredService<ActorMetadata.AccountActorFac>()

               let getAccountRef = AccountActor.get accountFac

               BillingCycleActor.scheduleMonthly
                  system
                  quartzPersistentARef
                  getAccountRef

               registry.Register<ActorMetadata.BillingCycleBulkWriteMarker>(
                  BillingCycleBulkWriteActor.start system {
                     saveBillingStatements = saveBillingStatements
                  }
                  |> untyped
               )

               registry.Register<ActorMetadata.EmailMarker>(
                  EmailActor.start system broadcast |> untyped
               )

               registry.Register<ActorMetadata.AccountClosureMarker>(
                  AccountClosureActor.start
                     system
                     quartzPersistentARef
                     getAccountRef
                  |> untyped
               )

               AccountClosureActor.scheduleNightlyCheck quartzPersistentARef

               registry.Register<ActorMetadata.DomesticTransferMarker>(
                  DomesticTransferRecipientActor.start system
                  <| provider.GetRequiredService<AccountBroadcast>()
                  <| getAccountRef
                  |> untyped
               )

               ())
#if DEBUG
            .AddStartup(StartupTask(fun sys reg -> seed sys))
#endif
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

   builder.Services.AddSingleton<ActorMetadata.AccountActorFac>(fun provider ->
      let system = provider.GetRequiredService<ActorSystem>()
      let broadcast = provider.GetRequiredService<AccountBroadcast>()
      let persistence = provider.GetRequiredService<AccountPersistence>()
      AccountActor.start persistence broadcast system)
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

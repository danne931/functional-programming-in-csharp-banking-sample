namespace Bank.Infrastructure

open System
open Microsoft.AspNetCore.SignalR
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Akka.Actor
open Akka.Event
open Akka.Hosting
open Akka.Remote.Hosting
open Akka.Cluster.Hosting
open Akka.Cluster.Sharding
open Akka.Persistence.Hosting
open Akka.Persistence.Sql.Hosting
open Akka.Quartz.Actor
open Quartz
open Akkling

open Bank.Account.Api
open Bank.Account.Domain
open Bank.BillingCycle.Api
open ActorUtil
open Bank.Hubs

module AkkaInfra =
   let private getJournalOpts () =
      let journalOpts = SqlJournalOptions()

      journalOpts.ConnectionString <-
         Env.config.ConnectionStrings.PostgresAdoFormat

      journalOpts.ProviderName <- Env.config.AkkaPersistence.DbProvider
      journalOpts.AutoInitialize <- true
      let jdo = JournalDatabaseOptions(DatabaseMapping.Default)
      let jto = JournalTableOptions()
      jto.TableName <- Env.config.AkkaPersistence.JournalTableName
      jto.UseWriterUuidColumn <- true
      jdo.JournalTable <- jto
      journalOpts.DatabaseOptions <- jdo

      journalOpts.Adapters.AddEventAdapter<AkkaPersistenceEventAdapter>(
         "v1",
         [ typedefof<AccountMessage> ]
      )
      |> ignore

      journalOpts

   let private getSnapshotOpts () =
      let snapshotOpts = SqlSnapshotOptions()

      snapshotOpts.ConnectionString <-
         Env.config.ConnectionStrings.PostgresAdoFormat

      snapshotOpts.ProviderName <- Env.config.AkkaPersistence.DbProvider
      snapshotOpts.AutoInitialize <- true
      let sdo = SnapshotDatabaseOptions(DatabaseMapping.Default)
      let sto = SnapshotTableOptions()
      sto.TableName <- Env.config.AkkaPersistence.SnapshotTableName
      sdo.SnapshotTable <- sto
      snapshotOpts.DatabaseOptions <- sdo
      snapshotOpts

   let private injectDependencies (builder: WebApplicationBuilder) =
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

      ()

   let start
      (builder: WebApplicationBuilder)
      (configureInstrumentation:
         AkkaConfigurationBuilder -> IServiceProvider -> _)
      =
      injectDependencies builder

      let journalOpts = getJournalOpts ()
      let snapshotOpts = getSnapshotOpts ()

      builder.Services.AddAkka(
         Env.config.AkkaSystemName,
         (fun builder provider ->
            configureInstrumentation builder provider

            builder
               .AddHocon(
                  """
                  billing-cycle-bulk-write-mailbox: {
                     mailbox-type: "BillingCycleBulkWriteActor+PriorityMailbox, BillingCycle.App"
                  }
                  """,
                  HoconAddMode.Prepend
               )
               .WithRemoting(
                  Env.config.AkkaRemoteHost,
                  Env.config.AkkaRemotePort
               )
               .WithClustering(
                  ClusterOptions(
                     SeedNodes = List.toArray Env.config.AkkaClusterSeedNodes
                  )
               )
               .WithSqlPersistence(
                  Env.config.ConnectionStrings.PostgresAdoFormat,
                  Env.config.AkkaPersistence.DbProvider,
                  PersistenceMode.Both
               )
               .WithJournalAndSnapshot(journalOpts, snapshotOpts)
               .WithCustomSerializer(
                  "json",
                  [ typedefof<String>; typedefof<Object> ],
                  fun system ->
                     Akka.Serialization.NewtonSoftJsonSerializer(system)
               )
               .WithCustomSerializer(
                  "akka",
                  [ typedefof<AccountMessage>; typedefof<AccountState> ],
                  fun system -> AkkaSerializer(system)
               )
               .WithShardRegion<ActorMetadata.AccountShardRegionMarker>(
                  ActorMetadata.accountShardRegion.name,
                  (fun _ ->
                     let props =
                        AccountActor.initProps
                        <| provider.GetRequiredService<AccountPersistence>()
                        <| provider.GetRequiredService<AccountBroadcast>()
                        <| provider.GetRequiredService<ActorSystem>()

                     props.ToProps()),
                  ActorMetadata.accountShardRegion.messageExtractor,
                  ShardOptions(
                     StateStoreMode = StateStoreMode.DData,
                     RememberEntities = true,
                     RememberEntitiesStore = RememberEntitiesStore.Eventsourced
                  )
               )
               .WithActors(fun system registry ->
                  let broadcast =
                     provider.GetRequiredService<AccountBroadcast>()

                  let deadLetterHandler
                     (ctx: Actor<_>)
                     (msg: AllDeadLetters)
                     =
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

                  registry.Register<QuartzPersistentActor>(
                     quartzPersistentARef
                  )

                  let getAccountRef = AccountActor.get system

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

                  AccountClosureActor.scheduleNightlyCheck
                     quartzPersistentARef

                  registry.Register<ActorMetadata.DomesticTransferMarker>(
                     DomesticTransferRecipientActor.start
                        system
                        broadcast
                        getAccountRef
                     |> untyped
                  )

                  ())
            |> ignore

            ())
      )
      |> ignore

      ()

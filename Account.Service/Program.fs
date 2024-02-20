open Microsoft.Extensions.DependencyInjection
open Akka.Actor
open Akka.Event
open Akka.Hosting
open Akka.Cluster.Hosting
open Akka.Persistence.Hosting
open Akka.Persistence.Sql.Hosting
open Akka.Routing
open Akkling

open Bank.Account.Domain
open Bank.Account.Api
open Bank.Transfer.Api
open Bank.BillingCycle.Api
open Bank.Infrastructure
open ActorUtil
open BillingStatement

let builder = Env.builder

builder.Services.AddSingleton<AccountBroadcast>(fun provider ->
   AccountBroadcaster.init <| provider.GetRequiredService<ActorSystem>())
|> ignore

let journalOpts = AkkaInfra.getJournalOpts ()

journalOpts.Adapters.AddEventAdapter<AkkaPersistenceEventAdapter>(
   "v1",
   [ typedefof<AccountMessage> ]
)
|> ignore

let snapshotOpts = AkkaInfra.getSnapshotOpts ()

builder.Services.AddAkka(
   Env.config.AkkaSystemName,
   (fun builder provider ->
      let initConfig =
         AkkaInfra.withClustering [|
            ClusterMetadata.roles.account
            ClusterMetadata.roles.signalR
         |]
         << AkkaInfra.withPetabridgeCmd
         << AkkaInfra.withHealthCheck
         << AkkaInfra.withLogging

      (initConfig builder)
         .WithSqlPersistence(
            Env.config.ConnectionStrings.PostgresAdoFormat,
            Env.config.AkkaPersistence.DbProvider,
            PersistenceMode.Both
         )
         .WithJournalAndSnapshot(journalOpts, snapshotOpts)
         .WithCustomSerializer(
            BankSerializer.Name,
            [ typedefof<obj> ],
            fun system -> BankSerializer(system)
         )
         .WithDistributedPubSub(ClusterMetadata.roles.signalR)
         .WithSingletonProxy<ActorMetadata.SchedulingMarker>(
            ActorMetadata.scheduling.Name,
            ClusterSingletonOptions(Role = ClusterMetadata.roles.scheduling)
         )
         .WithShardRegion<ActorMetadata.AccountMarker>(
            ClusterMetadata.accountShardRegion.name,
            (fun persistenceId ->
               let props =
                  AccountActor.initProps
                  <| provider.GetRequiredService<AccountBroadcast>()
                  <| provider.GetRequiredService<ActorSystem>()
                  <| Env.config.AccountActorSupervisor
                  <| persistenceId

               props),
            ClusterMetadata.accountShardRegion.messageExtractor,
            ShardOptions(Role = ClusterMetadata.roles.account)
         )
         .WithSingleton<ActorMetadata.BillingCycleMarker>(
            ActorMetadata.billingCycle.Name,
            (fun system _ resolver ->
               let typedProps =
                  BillingCycleActor.actorProps
                  <| Env.config.BillingCycleFanoutThrottle
                  <| AccountActor.get system

               typedProps.ToProps()),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithSingleton<ActorMetadata.TransferProgressTrackingMarker>(
            ActorMetadata.transferProgressTracking.Name,
            (fun system _ _ ->
               let typedProps =
                  TransferProgressTrackingActor.actorProps
                     system
                     DomesticTransferRecipientActor.get
                     (getProgressCheckReadyDomesticTransfers
                        EnvTransfer.config.TransferProgressLookbackMinutes)
                     EnvTransfer.config.TransferProgressTrackingThrottle

               typedProps.ToProps()),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithSingleton<ActorMetadata.AccountClosureMarker>(
            ActorMetadata.accountClosure.Name,
            (fun system registry _ ->
               let typedProps =
                  AccountClosureActor.initProps system
                  <| SchedulingActor.get registry
                  <| AccountActor.get system
                  <| Env.config.AccountDeleteThrottle

               typedProps.ToProps()),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithSingleton<ActorMetadata.AccountEventConsumerMarker>(
            ActorMetadata.accountEventConsumer.Name,
            (fun system _ _ ->
               let typedProps =
                  AccountEventConsumerActor.actorProps
                     (AccountActor.get system)
                     Env.config.AccountEventProjectionChunking
                     Env.config.AccountEventReadModelPersistenceBackoffRestart
                     Env.config.AccountEventReadModelRetryPersistenceAfter
                     upsertAccounts

               typedProps.ToProps()),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         // TODO: Do more than just printing dead letter messages.
         .WithSingleton<ActorMetadata.AuditorMarker>(
            ActorMetadata.auditor.Name,
            (fun system _ _ ->
               let handler (ctx: Actor<_>) =
                  EventStreaming.subscribe ctx.Self system.EventStream
                  |> ignore

                  logInfo ctx "Auditor subscribed to system event stream"

                  let rec loop () = actor {
                     let! (msg: AllDeadLetters) = ctx.Receive()
                     logError ctx $"Dead letter: {msg}"
                     return! loop ()
                  }

                  loop ()

               (props handler).ToProps()),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithSingleton<ActorMetadata.AccountSeederMarker>(
            ActorMetadata.accountSeeder.Name,
            (fun system _ _ ->
               let typedProps =
                  AccountSeederActor.actorProps <| AccountActor.get system

               typedProps.ToProps()),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithSingleton<ActorMetadata.CircuitBreakerMarker>(
            ActorMetadata.circuitBreaker.Name,
            (fun _ _ _ ->
               CircuitBreakerActor.initProps
                  Env.config.CircuitBreakerActorSupervisor),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithActors(fun system registry ->
            let routerEnv = EnvTransfer.config.DomesticTransferRouter
            let broadcast = provider.GetRequiredService<AccountBroadcast>()
            let getAccountRef = AccountActor.get system

            let resize = DefaultResizer(1, routerEnv.MaxInstancesPerNode)
            let router = RoundRobinPool(1, resize)

            registry.Register<ActorMetadata.DomesticTransferMarker>(
               DomesticTransferRecipientActor.start
                  system
                  broadcast
                  getAccountRef
                  (EnvTransfer.config.domesticTransferCircuitBreaker system)
                  router
               |> untyped
            )

            registry.Register<ActorMetadata.BillingStatementMarker>(
               BillingStatementActor.start
                  system
                  {
                     saveBillingStatements = saveBillingStatements
                  }
                  Env.config.BillingStatementPersistenceChunking
                  Env.config.BillingStatementPersistenceBackoffRestart
                  Env.config.BillingStatementRetryPersistenceAfter
               |> untyped
            )

            registry.Register<ActorMetadata.EmailMarker>(
               EmailActor.start
                  system
                  broadcast
                  EnvNotifications.config.EmailThrottle
                  (EnvNotifications.config.circuitBreaker system)
               |> untyped
            )

            ())
         .AddStartup(
            StartupTask(fun _ registry -> task {
               if not Env.isProd then
                  AccountSeederActor.get registry
                  <! AccountSeederMessage.SeedAccounts
            })
         )
      |> ignore

      ())
)
|> ignore

let host = builder.Build()
host.Run()

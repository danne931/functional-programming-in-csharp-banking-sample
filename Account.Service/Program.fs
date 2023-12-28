open Microsoft.Extensions.DependencyInjection
open System
open Akka.Actor
open Akka.Event
open Akka.Hosting
open Akka.Cluster.Hosting
open Akka.Cluster.Sharding
open Akka.Persistence.Hosting
open Akka.Persistence.Sql.Hosting
open Akka.Routing
open Akkling

open Bank.Account.Domain
open Bank.Infrastructure
open Scheduler.Infrastructure
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
         .AddHocon(
            """
            billing-statement-mailbox: {
               mailbox-type: "BillingStatementActor+PriorityMailbox, Billing.App"
            }
            """,
            HoconAddMode.Prepend
         )
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
            (fun _ ->
               let props =
                  AccountActor.initProps
                  <| provider.GetRequiredService<AccountBroadcast>()
                  <| provider.GetRequiredService<ActorSystem>()

               props.ToProps()),
            ClusterMetadata.accountShardRegion.messageExtractor,
            ShardOptions(
               Role = ClusterMetadata.roles.account,
               StateStoreMode = StateStoreMode.DData,
               RememberEntities = true,
               RememberEntitiesStore = RememberEntitiesStore.Eventsourced
            )
         )
         .WithSingleton<ActorMetadata.EmailMarker>(
            ActorMetadata.email.Name,
            (fun system _ resolver ->
               let broadcast = resolver.GetService<AccountBroadcast>()
               let typedProps = EmailActor.initProps system broadcast
               typedProps.ToProps()),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithSingleton<ActorMetadata.BillingCycleMarker>(
            ActorMetadata.billingCycle.Name,
            (fun system _ resolver ->
               let typedProps =
                  BillingCycleActor.actorProps
                  <| Env.config.BillingCycleFanoutThrottle
                  <| AccountActor.get system
                  <| resolver.GetService<AccountBroadcast>()

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
               let typedProps = CircuitBreakerActor.actorProps ()

               typedProps.ToProps()),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithActors(fun system registry ->
            let routerEnv = EnvTransfer.config.DomesticTransferRouter
            let breakerEnv = EnvTransfer.config.DomesticTransferCircuitBreaker
            let broadcast = provider.GetRequiredService<AccountBroadcast>()
            let getAccountRef = AccountActor.get system

            let resize = DefaultResizer(1, routerEnv.MaxInstancesPerNode)
            let router = RoundRobinPool(1, resize)

            let breaker =
               Akka.Pattern.CircuitBreaker(
                  system.Scheduler,
                  maxFailures = breakerEnv.MaxFailures,
                  callTimeout = TimeSpan.FromSeconds breakerEnv.CallTimeout,
                  resetTimeout = TimeSpan.FromSeconds breakerEnv.ResetTimeout
               )

            registry.Register<ActorMetadata.DomesticTransferMarker>(
               DomesticTransferRecipientActor.start
                  system
                  broadcast
                  getAccountRef
                  breaker
                  router
               |> untyped
            )

            registry.Register<ActorMetadata.BillingStatementMarker>(
               BillingStatementActor.start system |> untyped
            )

            ())
         .AddStartup(
            StartupTask(fun _ registry -> task {
               if Env.isDev then
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

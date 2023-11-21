open Microsoft.Extensions.DependencyInjection
open System.Threading.Tasks
open Akka.Actor
open Akka.Event
open Akka.Hosting
open Akka.Cluster.Hosting
open Akka.Cluster.Sharding
open Akka.Cluster.Routing
open Akka.Persistence.Hosting
open Akka.Persistence.Sql.Hosting
open Akkling

open Bank.Account.Domain
open Bank.Infrastructure
open ActorUtil
open BillingStatement

let builder = Env.builder

builder.Services.AddSingleton<AccountBroadcast>(fun provider ->
   SignalRProxy.init <| provider.GetRequiredService<ActorSystem>())
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

      (initConfig builder)
         .AddHocon(
            """
            billing-cycle-bulk-write-mailbox: {
               mailbox-type: "BillingCycleBulkWriteActor+PriorityMailbox, BillingCycle.App"
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
            [
               typedefof<AccountMessage>
               typedefof<AccountState>
               typedefof<SignalRMessage>
               typedefof<BillingMessage>
               typedefof<AccountClosureMessage>
               typedefof<EmailActor.EmailMessage>
               // TODO:
               // Temporary until Akka.Quartz.Actor supports specifying
               // custom serializers for more precise types.
               // If fixed, will be able to replace typedefof<obj> with,
               // for instance, typedefof<BillingMessage> for
               // messages serialized for Quartz jobs.
               // https://github.com/akkadotnet/Akka.Quartz.Actor/issues/215
               typedefof<obj>
            ],
            fun system -> BankSerializer(system)
         )
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
         .WithSingleton<ActorMetadata.BillingCycleBulkWriteMarker>(
            ActorMetadata.billingCycleBulkWrite.Name,
            (fun system _ resolver ->
               let typedProps =
                  BillingCycleBulkWriteActor.initProps
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
         .WithDistributedPubSub(ClusterMetadata.roles.signalR)
         .WithActors(fun system registry ->
            let broadcast = provider.GetRequiredService<AccountBroadcast>()
            let getAccountRef = AccountActor.get system

            registry.Register<ActorMetadata.DomesticTransferMarker>(
               DomesticTransferRecipientActor.start
                  system
                  broadcast
                  getAccountRef
               |> untyped
            )

            ())
         .AddStartup(
            StartupTask(fun system _ ->
               if Env.isDev then
                  PostgresSeeder.seed system
               else
                  Task.FromResult())
         )
      |> ignore

      ())
)
|> ignore

let host = builder.Build()
host.Run()

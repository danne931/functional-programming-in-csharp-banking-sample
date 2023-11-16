open Microsoft.Extensions.DependencyInjection
open Akka.Actor
open Akka.Event
open Akka.Hosting
open Akka.Cluster.Hosting
open Akka.Cluster.Sharding
open Akka.Persistence.Hosting
open Akka.Persistence.Sql.Hosting
open Akka.Quartz.Actor
open Quartz
open Akkling
open Petabridge.Cmd.Host
open Petabridge.Cmd.Cluster
open Petabridge.Cmd.Cluster.Sharding

open Bank.Account.Domain
open Bank.BillingCycle.Api
open Bank.Infrastructure
open ActorUtil

let builder = Env.builder

builder.Services.AddSingleton<AccountBroadcast>(fun provider ->
   SignalRProxy.init <| provider.GetRequiredService<ActorSystem>())
|> ignore

QuartzInfra.start builder

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
      let builder =
         AkkaInfra.withClustering builder [| "account-role"; "signal-r-role" |]

      builder
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
            "akka",
            [
               typedefof<AccountMessage>
               typedefof<AccountState>
               typedefof<SignalRMessage>
               // TODO:
               // Temporary until Akka.Quartz.Actor supports specifying
               // custom serializers for more precise types.
               // If fixed, will be able to replace typedefof<obj> with,
               // for instance, typedefof<BillingCycleActor.Message> for
               // messages serialized for Quartz jobs.
               // https://github.com/akkadotnet/Akka.Quartz.Actor/issues/215
               typedefof<obj>
            ],
            fun system -> AkkaSerializer(system)
         )
         .WithShardRegion<ActorMetadata.AccountShardRegionMarker>(
            ActorMetadata.accountShardRegion.name,
            (fun _ ->
               let props =
                  AccountActor.initProps
                  <| provider.GetRequiredService<AccountBroadcast>()
                  <| provider.GetRequiredService<ActorSystem>()

               props.ToProps()),
            ActorMetadata.accountShardRegion.messageExtractor,
            ShardOptions(
               Role = "account-role",
               StateStoreMode = StateStoreMode.DData,
               RememberEntities = true,
               RememberEntitiesStore = RememberEntitiesStore.Eventsourced
            )
         )
         .WithDistributedPubSub("signal-r-role")
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

            AccountClosureActor.scheduleNightlyCheck quartzPersistentARef

            registry.Register<ActorMetadata.DomesticTransferMarker>(
               DomesticTransferRecipientActor.start
                  system
                  broadcast
                  getAccountRef
               |> untyped
            )

            ())
         .AddPetabridgeCmd(fun cmd ->
            cmd.RegisterCommandPalette(ClusterCommands.Instance) |> ignore

            cmd.RegisterCommandPalette(ClusterShardingCommands.Instance)
            |> ignore)
#if DEBUG
         .AddStartup(StartupTask(fun sys _ -> PostgresSeeder.seed sys))
#endif
      |> ignore

      ())
)
|> ignore

let host = builder.Build()
host.Run()

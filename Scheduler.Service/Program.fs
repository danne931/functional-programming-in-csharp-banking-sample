open Microsoft.Extensions.DependencyInjection
open Akka.Hosting
open Akka.Cluster.Hosting
open Akka.Quartz.Actor
open Akkling
open Quartz

open Bank.Infrastructure
open ActorUtil

let builder = Env.builder

builder.Services.AddQuartzHostedService(fun opts ->
   // Interrupt shutdown & wait for executing jobs to finish first.
   opts.WaitForJobsToComplete <- true
   // Avoid running jobs until application started
   opts.AwaitApplicationStarted <- true)
|> ignore

builder.Services
   .AddOptions<QuartzOptions>()
   .Configure(fun opts ->
      opts.SchedulerName <- Env.config.Quartz.SchedulerName
      opts.Scheduling.OverWriteExistingData <- false
      // Attempts to add a job with a name of a job already
      // scheduled will be ignored.
      opts.Scheduling.IgnoreDuplicates <- true
      ())
   .Services.AddQuartz(fun q ->
      q.UsePersistentStore(fun store ->
         store.SetProperty(
            "quartz.jobStore.tablePrefix",
            Env.config.Quartz.TablePrefix
         )

         store.UsePostgres(Env.config.ConnectionStrings.PostgresAdoFormat)

         store.UseNewtonsoftJsonSerializer()

         store.PerformSchemaValidation <- true

         store.SetProperty(
            "quartz.plugin.shutdownhook.type",
            "Quartz.Plugin.Management.ShutdownHookPlugin, Quartz.Plugins"
         )

         store.SetProperty("quartz.plugin.shutdownhook.cleanShutdown", "true")

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

builder.Services.AddSingleton<IScheduler>(
   builder.Services
      .BuildServiceProvider()
      .GetRequiredService<ISchedulerFactory>()
      .GetScheduler()
      .Result
)
|> ignore

builder.Services.AddAkka(
   Env.config.AkkaSystemName,
   (fun builder _ ->
      let initConfig =
         AkkaInfra.withClustering [| ClusterMetadata.roles.scheduling |]
         << AkkaInfra.withPetabridgeCmd

      (initConfig builder)
         .WithCustomSerializer(
            BankSerializer.Name,
            [
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
         .WithSingletonProxy<ActorMetadata.AccountClosureMarker>(
            ActorMetadata.accountClosure.Name,
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithSingletonProxy<ActorMetadata.BillingCycleBulkWriteMarker>(
            ActorMetadata.billingCycleBulkWrite.Name,
            ClusterSingletonOptions(Role = ClusterMetadata.roles.account)
         )
         .WithSingleton<QuartzPersistentActor>(
            "QuartzPersistentActor",
            (fun _ _ resolver ->
               let scheduler = resolver.GetService<IScheduler>()

               Akka.Actor.Props.Create(fun () ->
                  QuartzPersistentActor scheduler)),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.scheduling)
         )
         .WithSingleton<ActorMetadata.SchedulingMarker>(
            ActorMetadata.scheduling.Name,
            (fun _ registry _ ->
               let typedProps =
                  SchedulingActor.actorProps
                  <| registry.Get<QuartzPersistentActor>()

               typedProps.ToProps()),
            ClusterSingletonOptions(Role = ClusterMetadata.roles.scheduling)
         )
         .AddStartup(
            StartupTask(fun _ registry -> task {
               let schedulingActor = SchedulingActor.get registry
               schedulingActor <! SchedulingActor.BillingCycleCronJobSchedule

               schedulingActor
               <! SchedulingActor.AccountClosureCronJobSchedule
            })
         )
      |> ignore

      ())
)
|> ignore

let host = builder.Build()
host.Run()

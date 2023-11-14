namespace Bank.Infrastructure

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Quartz

module QuartzInfra =
   let start (builder: WebApplicationBuilder) =
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

               store.UsePostgres(
                  Env.config.ConnectionStrings.PostgresAdoFormat
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

      builder.Services.AddSingleton<IScheduler>(
         builder.Services
            .BuildServiceProvider()
            .GetRequiredService<ISchedulerFactory>()
            .GetScheduler()
            .Result
      )
      |> ignore

[<RequireQualifiedAccess>]
module AccountClosureTriggers

open Quartz
open System

let deleteAccounts (logInfo: string -> unit) =
   let name = "AccountClosureDeletion"
   let group = "Bank"

   let builder =
      TriggerBuilder
         .Create()
         .ForJob($"{name}Job", group)
         .WithIdentity($"{name}Trigger", group)
         .WithDescription(
            "Delete remaining user data 3 months after requested account closure"
         )

   if Env.isProd then
      logInfo "Scheduling account deletion for 3 months from now."
      builder.StartAt(DateTimeOffset(DateTime.Now).AddDays(90)).Build()
   else
      logInfo "Scheduling account deletion for now."
      builder.StartNow().Build()

let scheduleNightlyCheck (logInfo: string -> unit) =
   let name = "AccountClosureNightlyCheck"
   let group = "Bank"

   let builder =
      TriggerBuilder
         .Create()
         .ForJob($"{name}Job", group)
         .WithIdentity($"{name}Trigger", group)
         .WithDescription("Nightly check for requested account closures")

   if Env.isProd then
      logInfo "Scheduling account closure check nightly at 10:30PM"
      builder.WithCronSchedule("0 30 10 ? * * *").Build()
   else
      logInfo "Scheduling account closure check every 3 minutes"

      builder
         .WithSimpleSchedule(fun s ->
            s.WithIntervalInMinutes(3).RepeatForever() |> ignore)
         .Build()

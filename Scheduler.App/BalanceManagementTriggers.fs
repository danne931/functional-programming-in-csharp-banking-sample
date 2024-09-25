[<RequireQualifiedAccess>]
module BalanceManagementTriggers

open Quartz

open AutomaticTransfer

let schedule (logInfo: string -> unit) (schedule: CronSchedule) =
   let group = "Bank"

   let name, descriptionMsg, logMsg, cronSchedule =
      match schedule with
      | CronSchedule.Daily ->
         "BalanceManagementDaily", "daily", "daily at 8:00AM", "0 0 8 ? * * *"
      | CronSchedule.TwiceMonthly ->
         "BalanceManagementTwiceMonthly",
         "twice monthly",
         "for the 1st & 15th of the month at 8:00AM",
         "0 0 8 1,15 * ? *"

   let builder =
      TriggerBuilder
         .Create()
         .ForJob($"{name}Job", group)
         .WithIdentity($"{name}Trigger", group)
         .WithDescription(
            $"Accounts are checked for auto transfer rules {descriptionMsg}."
         )

   if Env.isProd then
      logInfo $"Scheduling balance management {logMsg}"

      builder.WithCronSchedule(cronSchedule).Build()
   else
      let minutes = 5
      logInfo $"Scheduling balance management for every {minutes} minutes."

      builder
         .WithSimpleSchedule(fun s ->
            s.WithIntervalInMinutes(minutes).RepeatForever() |> ignore)
         .Build()

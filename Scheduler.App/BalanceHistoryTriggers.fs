[<RequireQualifiedAccess>]
module BalanceHistoryTriggers

open Quartz

let scheduleNightly (logInfo: string -> unit) =
   let name = "BalanceHistory"
   let group = "Bank"

   let builder =
      TriggerBuilder
         .Create()
         .ForJob($"{name}Job", group)
         .WithIdentity($"{name}Trigger", group)
         .WithDescription("Balance history records are recorded daily.")

   logInfo
      "Scheduling daily balance history computation for 15 minutes after midnight."

   builder.WithCronSchedule("0 15 0 * * ?").Build()

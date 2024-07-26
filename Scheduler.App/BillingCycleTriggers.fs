[<RequireQualifiedAccess>]
module BillingCycleTriggers

open Quartz

let scheduleMonthly (logInfo: string -> unit) =
   let name = "BillingCycle"
   let group = "Bank"

   let builder =
      TriggerBuilder
         .Create()
         .ForJob($"{name}Job", group)
         .WithIdentity($"{name}Trigger", group)
         .WithDescription(
            "Accounts transactions are consolidated at the end of a billing cycle."
         )

   if Env.isProd then
      logInfo
         "Scheduling billing cycle for the last day of the month at 11:55PM"

      builder.WithCronSchedule("0 55 23 L * ? *").Build()
   else
      logInfo "Scheduling billing cycle for every 2 minutes."

      builder
         .WithSimpleSchedule(fun s ->
            s.WithIntervalInMinutes(20).RepeatForever() |> ignore)
         .Build()

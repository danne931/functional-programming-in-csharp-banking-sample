[<RequireQualifiedAccess>]
module BillingCycleTriggers

open Quartz

type Message = | BillingCycle

let scheduleMonthly () =
   let name = "BillingCycle"
   let group = "Bank"

   TriggerBuilder
      .Create()
      .ForJob($"{name}Job", group)
      .WithIdentity($"{name}Trigger", group)
      .WithDescription(
         "Accounts transactions are consolidated at the end of a billing cycle."
      )
#if DEBUG
      .WithSimpleSchedule(fun s ->
         s.WithIntervalInMinutes(2).RepeatForever() |> ignore)
#else
      .WithCronSchedule("0 55 23 L * ? *") // Last day of every month at 11:55PM
#endif
      .Build()

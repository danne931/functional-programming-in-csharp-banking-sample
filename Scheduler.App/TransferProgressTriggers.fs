[<RequireQualifiedAccess>]
module TransferProgressTrackingTriggers

open Quartz

let schedule (logInfo: string -> unit) =
   let name = "TransferProgressTracking"
   let group = "Bank"

   let builder =
      TriggerBuilder
         .Create()
         .ForJob($"{name}Job", group)
         .WithIdentity($"{name}Trigger", group)
         .WithDescription(
            "Regularly request transfer progress status updates from mock
            domestic transfer processor."
         )

   if Env.isProd then
      let hours = 1
      logInfo $"Scheduling transfer progress checking for every {hours} hours"

      builder
         .WithSimpleSchedule(fun s ->
            s.WithIntervalInHours(hours).RepeatForever() |> ignore)
         .Build()
   else
      let seconds = 60
      logInfo $"Scheduling transfer progress tracking every {seconds} seconds."

      builder
         .WithSimpleSchedule(fun s ->
            s.WithIntervalInSeconds(seconds).RepeatForever() |> ignore)
         .Build()

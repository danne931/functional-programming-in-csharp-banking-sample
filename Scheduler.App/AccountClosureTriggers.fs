[<RequireQualifiedAccess>]
module AccountClosureTriggers

open Quartz

let deleteAccounts () =
   let name = "AccountClosureDeletion"
   let group = "Bank"

   TriggerBuilder
      .Create()
      .ForJob($"{name}Job", group)
      .WithIdentity($"{name}Trigger", group)
      .WithDescription(
         "Delete remaining user data 3 months after requested account closure"
      )
#if DEBUG
      .StartNow()
#else
      .StartAt(DateTimeOffset(DateTime.Now).AddDays(90))
#endif
      .Build()

let scheduleNightlyCheck () =
   let name = "AccountClosureNightlyCheck"
   let group = "Bank"

   TriggerBuilder
      .Create()
      .ForJob($"{name}Job", group)
      .WithIdentity($"{name}Trigger", group)
      .WithDescription("Nightly check for requested account closures")
#if DEBUG
      .WithSimpleSchedule(fun s ->
         s.WithIntervalInMinutes(3).RepeatForever() |> ignore)
#else
      .WithCronSchedule("0 30 10 ? * * *") // Every night at 10:30PM
#endif
      .Build()

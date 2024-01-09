module Lib.Time

open System

type ComputedDuration = {
   Start: DateTime
   End: DateTime
   Duration: TimeSpan
} with

   static member empty = {
      Start = DateTime.MinValue
      End = DateTime.MinValue
      Duration = TimeSpan.Zero
   }

   member x.start() = { x with Start = DateTime.UtcNow }

   member x.computeDuration(endTime: DateTime option) =
      let endTime = endTime |> Option.defaultValue DateTime.UtcNow

      {
         x with
            End = endTime
            Duration = endTime - x.Start
      }

let IsToday (debitDate: DateTime) =
   let today = DateTime.UtcNow
   $"{today.Day}-{today.Month}-{today.Year}" = $"{debitDate.Day}-{debitDate.Month}-{debitDate.Year}"

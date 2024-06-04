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

module DateTime =
   let isToday (date: DateTime) =
      let today = DateTime.UtcNow

      DateTime(today.Year, today.Month, today.Day) = DateTime(
         date.Year,
         date.Month,
         date.Day
      )

   let parseOptional (date: string) =
      try
         Some
         <| DateTime.Parse(
            date,
            null,
            Globalization.DateTimeStyles.AdjustToUniversal
         )
      with _ ->
         None

   let formatRangeShort (dateStart: DateTime) (dateEnd: DateTime) =
      let withYear =
         dateStart.Year <> dateEnd.Year
         || dateStart.Year <> DateTime.UtcNow.Year
         || dateEnd.Year <> DateTime.UtcNow.Year

      let format (date: DateTime) =
         let formatted = $"{date.Month}/{date.Day}"
         if withYear then $"{formatted}/{date.Year}" else formatted

      format dateStart, format dateEnd

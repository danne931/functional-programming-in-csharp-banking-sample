module Lib.Time

open System

module DateTime =
   let isToday (date: DateTime) =
      let date = date.ToLocalTime().Date
      let today = DateTime.Now.Date
      date = today

   let isThisMonth (date: DateTime) =
      let date = date.ToLocalTime()
      let today = DateTime.Now
      (today.Month, today.Year) = (date.Month, date.Year)

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

   let numberToDisplayMonth =
      Map [
         1, "Jan"
         2, "Feb"
         3, "Mar"
         4, "Apr"
         5, "May"
         6, "Jun"
         7, "Jul"
         8, "Aug"
         9, "Sep"
         10, "Oct"
         11, "Nov"
         12, "Dec"
      ]

   let formatShort (date: DateTime) =
      $"{numberToDisplayMonth[date.Month]} {date.Day}"

   let formatRangeShort (dateStart: DateTime) (dateEnd: DateTime) =
      let withYear =
         dateStart.Year <> dateEnd.Year
         || dateStart.Year <> DateTime.UtcNow.Year
         || dateEnd.Year <> DateTime.UtcNow.Year

      let format (date: DateTime) =
         let formatted = $"{date.Month}/{date.Day}"
         if withYear then $"{formatted}/{date.Year}" else formatted

      format dateStart, format dateEnd

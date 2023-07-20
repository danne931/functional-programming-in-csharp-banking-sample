module Lib.Time

open System

let IsToday (debitDate: DateTime) =
   let today = DateTime.UtcNow
   $"{today.Day}-{today.Month}-{today.Year}" = $"{debitDate.Day}-{debitDate.Month}-{debitDate.Year}"

module CustomDateInterpreter

open System
open System.Text.RegularExpressions
open Validus
open Validus.Operators

open Lib.SharedTypes
open Lib.Time

[<RequireQualifiedAccess>]
type DateRangeSignifier =
   | Start
   | End

type PreliminaryDateParts = {
   Month: string option
   Day: string option
   Year: string option
}

let dayAliasPattern = @"^(yesterday|today)$"
let dayNumberPattern = @"^\d+$"
let dayWithOptionalSuffixPattern = @"^(\d+)(st|nd|rd|th)?$"

let identifyParts (input: string) : PreliminaryDateParts =
   let parts =
      input.Split(
         [| " "; ","; "/"; "-"; "." |],
         StringSplitOptions.RemoveEmptyEntries
      )

   if parts.Length >= 3 then
      {
         Month = Some parts.[0]
         Day = Some parts.[1]
         Year = Some parts.[2]
      }
   else if parts.Length = 2 && parts.[1].Length <= 2 then
      {
         Month = Some parts.[0]
         Day = Some parts.[1]
         Year = None
      }
   else if parts.Length = 2 && parts.[1].Length > 2 then
      if Regex(dayNumberPattern, RegexOptions.IgnoreCase).IsMatch(parts[1]) then
         {
            Month = Some parts.[0]
            Day = None
            Year = Some parts.[1]
         }
      else
         {
            Month = Some parts.[0]
            Day = Some parts.[1]
            Year = None
         }
   else if parts.Length = 1 then
      let part = parts.[0].ToLower()

      if Regex(dayAliasPattern, RegexOptions.IgnoreCase).IsMatch(part) then
         {
            Month = None
            Day = Some part
            Year = None
         }
      else
         {
            Month = Some part
            Day = None
            Year = None
         }
   else
      {
         Month = None
         Day = None
         Year = None
      }

let months =
   Map [
      "jan", 1
      "january", 1
      "feb", 2
      "february", 2
      "mar", 3
      "march", 3
      "apr", 4
      "april", 4
      "may", 5
      "jun", 6
      "june", 6
      "jul", 7
      "july", 7
      "aug", 8
      "august", 8
      "sep", 9
      "sept", 9
      "september", 9
      "oct", 10
      "october", 10
      "nov", 11
      "november", 11
      "dec", 12
      "december", 12
   ]

[<RequireQualifiedAccess>]
type ValidDay =
   | Number of int
   | Alias of string // "today", "yesterday"

type ValidMonth = ValidMonth of int

type ValidYear = ValidYear of int

let inputToValidatedDateFormat
   (day: ValidDay)
   (month: ValidMonth)
   (year: ValidYear)
   (signifier: DateRangeSignifier)
   : string * DateTime
   =
   let strFormat, date =
      match day with
      | ValidDay.Alias alias ->
         if alias.ToLower() = "yesterday" then
            alias, DateTime.Today.AddDays(-1)
         else
            alias, DateTime.Today
      | ValidDay.Number day ->
         let (ValidMonth month) = month
         let (ValidYear year) = year

         $"{DateTime.numberToDisplayMonth[month]} {day}, {year}",
         DateTime(year, month, day)

   match signifier with
   | DateRangeSignifier.Start -> strFormat, date
   | DateRangeSignifier.End -> strFormat, date.AddDays(1).AddMilliseconds(-1)

let identifyDayFromMonthAndYear
   (month: ValidMonth)
   (year: ValidYear)
   (signifier: DateRangeSignifier)
   =
   match signifier with
   | DateRangeSignifier.Start -> ValidDay.Number 1
   | DateRangeSignifier.End ->
      let (ValidMonth month) = month
      let (ValidYear year) = year
      DateTime.DaysInMonth(year, month) |> ValidDay.Number

let identifyYearWhenInputJustMonth
   (month: ValidMonth)
   (signifier: DateRangeSignifier)
   =
   let currYear = DateTime.Today.Year

   match signifier with
   | DateRangeSignifier.End -> ValidYear currYear
   | DateRangeSignifier.Start ->
      let (ValidMonth month) = month
      let isFutureMonth = month > DateTime.Today.Month

      if isFutureMonth then
         ValidYear(currYear - 1)
      else
         (ValidYear currYear)

let identifyYearWhenInputJustDayAndMonth
   (day: ValidDay)
   (month: ValidMonth)
   (signifier: DateRangeSignifier)
   =
   let currYear = DateTime.Today.Year

   match signifier with
   | DateRangeSignifier.End -> ValidYear currYear
   | DateRangeSignifier.Start ->
      let (ValidMonth month) = month
      let isFutureMonth = month > DateTime.Today.Month

      let isFutureDay =
         month = DateTime.Today.Month
         && (match day with
             | ValidDay.Number day when day > DateTime.Today.Day -> true
             | _ -> false)

      if isFutureMonth || isFutureDay then
         ValidYear(currYear - 1)
      else
         (ValidYear currYear)

let parseInt: Validator<string, int> =
   fun field input ->
      let regexMatch = Regex.Match(input, dayWithOptionalSuffixPattern)

      if regexMatch.Success then
         Ok <| Int32.Parse regexMatch.Groups.[1].Value
      else
         Error <| ValidationErrors.create field [ $"Invalid day {input}" ]

let dayTextValidator: Validator<string, ValidDay> =
   _.ToLower()
   >> Check.String.pattern @"^(yesterday|today)"
      *|* (_.ToUpper() >> ValidDay.Alias)

let dayNumberFirstPassValidator: Validator<string, ValidDay> =
   parseInt >=> (Check.Int.between 1 31) *|* ValidDay.Number

let dayValidator: Validator<string, ValidDay> =
   dayTextValidator <|> dayNumberFirstPassValidator

let dayNumberValidatorGivenMonthAndYear
   (month: ValidMonth)
   (year: ValidYear)
   : Validator<string, ValidDay>
   =
   let (ValidMonth month) = month
   let (ValidYear year) = year
   let daysInMonth = DateTime.DaysInMonth(year, month)
   parseInt >=> (Check.Int.between 1 daysInMonth) *|* ValidDay.Number

let monthTextValidator: Validator<string, ValidMonth> =
   fun field input ->
      match Map.tryFind (input.ToLower()) months with
      | None -> Error <| ValidationErrors.create field [ "Invalid month text" ]
      | Some monthNum -> monthNum |> ValidMonth |> Ok

let monthNumberValidator: Validator<string, ValidMonth> =
   parseInt >=> (Check.Int.between 1 12) *|* ValidMonth

let monthValidator field input : ValidationResult<ValidMonth> =
   (monthTextValidator <|> monthNumberValidator) field input
   |> Result.mapError (fun _ ->
      ValidationErrors.create field [ $"Invalid month {input}" ])

let yearValidator: Validator<string, ValidYear> =
   (fun field input ->
      let currYearLast2Digits =
         DateTime.Today.Year |> string |> _.Substring(2) |> int

      match Int32.TryParse input with
      | true, year when year >= 1900 && year <= 9999 -> Ok year
      | true, year when year >= 0 && year <= currYearLast2Digits ->
         Ok(2000 + year)
      | true, year when year > currYearLast2Digits && year <= 99 ->
         Ok(1900 + year)
      | _ -> Error <| ValidationErrors.create field [ "Invalid year" ])
   *|* ValidYear

let validateInput
   (input: string)
   (signifier: DateRangeSignifier)
   : ValidationResult<string * DateTime>
   =
   let parts = identifyParts input

   match parts.Month, parts.Day, parts.Year with
   | Some month, Some day, Some year -> validate {
      let! month = monthValidator "Month" month
      let! year = yearValidator "Year" year
      let! day = dayNumberValidatorGivenMonthAndYear month year "Day" day
      return inputToValidatedDateFormat day month year signifier
     }
   | Some month, Some dayInput, None -> validate {
      let! day = dayNumberFirstPassValidator "Day" dayInput
      let! month = monthValidator "Month" month
      let year = identifyYearWhenInputJustDayAndMonth day month signifier

      let! day = dayNumberValidatorGivenMonthAndYear month year "Day" dayInput

      return inputToValidatedDateFormat day month year signifier
     }
   | Some month, None, None -> validate {
      let! month = monthValidator "Month" month
      let year = identifyYearWhenInputJustMonth month signifier
      let day = identifyDayFromMonthAndYear month year signifier
      return inputToValidatedDateFormat day month year signifier
     }
   | None, Some dayInput, None -> validate {
      let! day = dayValidator "Day" dayInput
      let month = ValidMonth DateTime.Today.Month
      let year = ValidYear DateTime.Today.Year

      let isAlias =
         function
         | ValidDay.Alias _ -> true
         | _ -> false

      if isAlias day then
         return inputToValidatedDateFormat day month year signifier
      else
         let! day =
            dayNumberValidatorGivenMonthAndYear month year "Day" dayInput

         return inputToValidatedDateFormat day month year signifier
     }
   | Some month, None, Some year -> validate {
      let! month = monthValidator "Month" month
      let! year = yearValidator "Year" year

      return inputToValidatedDateFormat (ValidDay.Number 1) month year signifier
     }
   | _ -> Error(ValidationErrors.create "" [ "Requires a formatted date." ])

let validate
   (input: string)
   (signifier: DateRangeSignifier)
   : Result<string * DateTime, string>
   =
   validateInput input signifier |> validationErrorsHumanFriendly

module CustomDateRange

open Feliz
open Feliz.UseElmish
open Elmish
open System
open System.Text.RegularExpressions
open Validus
open Validus.Operators

open Lib.SharedTypes
open Lib.Time

module Interpreter =
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
         if
            Regex(dayNumberPattern, RegexOptions.IgnoreCase).IsMatch(parts[1])
         then
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
         | None ->
            Error <| ValidationErrors.create field [ "Invalid month text" ]
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

         let! day =
            dayNumberValidatorGivenMonthAndYear month year "Day" dayInput

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

         return
            inputToValidatedDateFormat (ValidDay.Number 1) month year signifier
        }
      | _ -> Error(ValidationErrors.create "" [ "Requires a formatted date." ])

   let validate
      (input: string)
      (signifier: DateRangeSignifier)
      : Result<string * DateTime, string>
      =
      validateInput input signifier |> validationErrorsHumanFriendly

type State = {
   Input: {| Start: string; End: string |}
   ValidatedDate: {| Start: DateTime; End: DateTime |}
   ValidationError: {|
      Start: string option
      End: string option
      Range: string option
   |}
   ValidateOnKeyPress: {| Start: bool; End: bool |}
   DateRangeSubmission: DateTime * DateTime
}

type Msg =
   | SetStart of string
   | SetEnd of string
   | SubmitStart
   | SubmitEnd

let init (startDate: DateTime) (endDate: DateTime) () =
   let startInput, startDate =
      Interpreter.inputToValidatedDateFormat
         (Interpreter.ValidDay.Number startDate.Day)
         (Interpreter.ValidMonth startDate.Month)
         (Interpreter.ValidYear startDate.Year)
         Interpreter.DateRangeSignifier.Start

   let endInput, endDate =
      Interpreter.inputToValidatedDateFormat
         (Interpreter.ValidDay.Number endDate.Day)
         (Interpreter.ValidMonth endDate.Month)
         (Interpreter.ValidYear endDate.Year)
         Interpreter.DateRangeSignifier.End

   {
      Input = {| Start = startInput; End = endInput |}
      ValidatedDate = {| Start = startDate; End = endDate |}
      ValidationError = {|
         Start = None
         End = None
         Range = None
      |}
      ValidateOnKeyPress = {| Start = false; End = false |}
      DateRangeSubmission = startDate, endDate
   },
   Cmd.none

let stateWithRangeVerification (state: State) =
   if state.ValidatedDate.Start < state.ValidatedDate.End then
      {
         state with
            ValidationError.Range = None
      }
   else
      let startFormatted, endFormatted =
         DateTime.formatRangeShort
            state.ValidatedDate.Start
            state.ValidatedDate.End

      {
         state with
            ValidationError.Range =
               $"Start date {startFormatted} must precede end date {endFormatted}."
               |> Some
            ValidateOnKeyPress.Start = true
            ValidateOnKeyPress.End = true
      }

let update (onValidDateRange: DateTime * DateTime -> unit) msg state =
   match msg with
   | Msg.SetStart input ->
      let state = { state with Input.Start = input }

      let state =
         if state.ValidateOnKeyPress.Start then
            match
               Interpreter.validate input Interpreter.DateRangeSignifier.Start
            with
            | Ok(_, date) ->
               {
                  state with
                     ValidationError.Start = None
                     ValidatedDate.Start = date
               }
               |> stateWithRangeVerification
            | Error errMsg -> {
               state with
                  ValidationError.Start = Some errMsg
              }
         else
            state

      state, Cmd.none
   | Msg.SetEnd input ->
      let state = { state with Input.End = input }

      let state =
         if state.ValidateOnKeyPress.End then
            match
               Interpreter.validate input Interpreter.DateRangeSignifier.End
            with
            | Ok(_, date) ->
               {
                  state with
                     ValidationError.End = None
                     ValidatedDate.End = date
               }
               |> stateWithRangeVerification
            | Error errMsg -> {
               state with
                  ValidationError.End = Some errMsg
              }
         else
            state

      state, Cmd.none
   | Msg.SubmitStart ->
      match
         Interpreter.validate
            state.Input.Start
            Interpreter.DateRangeSignifier.Start
      with
      | Ok(formatted, startDate) ->
         let oldState = state

         let state =
            {
               state with
                  Input.Start = formatted
                  ValidatedDate.Start = startDate
            }
            |> stateWithRangeVerification

         let state =
            match state.ValidationError.End, state.ValidationError.Range with
            | None, None -> {
               state with
                  DateRangeSubmission =
                     state.ValidatedDate.Start, state.ValidatedDate.End
              }
            | _ -> state

         if state.DateRangeSubmission <> oldState.DateRangeSubmission then
            onValidDateRange state.DateRangeSubmission

         state, Cmd.none
      | Error errMsg ->
         {
            state with
               Input.Start = state.Input.Start
               ValidationError.Start = Some errMsg
               ValidateOnKeyPress.Start = true
         },
         Cmd.none
   | Msg.SubmitEnd ->
      match
         Interpreter.validate state.Input.End Interpreter.DateRangeSignifier.End
      with
      | Ok(formatted, endDate) ->
         let oldState = state

         let state =
            {
               state with
                  Input.End = formatted
                  ValidatedDate.End = endDate
            }
            |> stateWithRangeVerification

         let state =
            match state.ValidationError.Start, state.ValidationError.Range with
            | None, None -> {
               state with
                  DateRangeSubmission =
                     state.ValidatedDate.Start, state.ValidatedDate.End
              }
            | _ -> state

         if state.DateRangeSubmission <> oldState.DateRangeSubmission then
            onValidDateRange state.DateRangeSubmission

         state, Cmd.none
      | Error errMsg ->
         {
            state with
               Input.End = state.Input.End
               ValidationError.End = Some errMsg
               ValidateOnKeyPress.End = true
         },
         Cmd.none

[<ReactComponent>]
let CustomDateRangeComponent
   (startDate: DateTime)
   (endDate: DateTime)
   (onValidDateRange: DateTime * DateTime -> unit)
   =
   let state, dispatch =
      React.useElmish (init startDate endDate, update onValidDateRange, [||])

   classyNode Html.fieldSet [ "grid" ] [
      Html.label [
         Html.text "Date start"

         Html.input [
            attr.type' "text"
            attr.name "dateStart"

            attr.value state.Input.Start

            attr.onChange (fun (dateStart: string) ->
               dispatch <| Msg.SetStart dateStart)

            attr.onBlur (fun _ ->
               if state.ValidationError.Start.IsNone then
                  dispatch Msg.SubmitStart)

            if state.ValidationError.Start.IsSome then
               attr.ariaInvalid true
         ]

         match state.ValidationError.Start with
         | Some errMsg -> Html.small [ attr.text errMsg ]
         | None -> ()
      ]

      Html.label [
         Html.text "End date"

         Html.input [
            attr.type' "text"
            attr.name "dateEnd"

            attr.value state.Input.End

            attr.onChange (fun (dateEnd: string) ->
               dispatch <| Msg.SetEnd dateEnd)

            attr.onBlur (fun _ ->
               if state.ValidationError.End.IsNone then
                  dispatch Msg.SubmitEnd)

            if
               state.ValidationError.End.IsSome
               || state.ValidationError.Range.IsSome
            then
               attr.ariaInvalid true
         ]

         match state.ValidationError.End, state.ValidationError.Range with
         | Some errMsg, Some _
         | None, Some errMsg
         | Some errMsg, None -> Html.small [ attr.text errMsg ]
         | None, None -> ()
      ]
   ]

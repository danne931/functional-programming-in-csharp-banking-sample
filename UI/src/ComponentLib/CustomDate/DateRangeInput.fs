module CustomDateRangeInput

open Feliz
open Feliz.UseElmish
open Elmish
open System

open Lib.Time

module Interpreter = CustomDateInterpreter

let private validateStart = Interpreter.validate Interpreter.DateSignifier.Start
let private validateEnd = Interpreter.validate Interpreter.DateSignifier.End

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
         Interpreter.DateSignifier.Start

   let endInput, endDate =
      Interpreter.inputToValidatedDateFormat
         (Interpreter.ValidDay.Number endDate.Day)
         (Interpreter.ValidMonth endDate.Month)
         (Interpreter.ValidYear endDate.Year)
         Interpreter.DateSignifier.End

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
            match validateStart input with
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
            match validateEnd input with
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
      match validateStart state.Input.Start with
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
      match validateEnd state.Input.End with
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
let DateRangeInputComponent
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

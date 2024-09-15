module CustomDateInput

open Feliz
open Feliz.UseElmish
open Elmish
open System

open Lib.Validators
open Lib.SharedTypes

module Interpreter = CustomDateInterpreter

type State = {
   Input: string
   ValidationError: string option
   ValidateOnKeyPress: bool
   DateSubmission: DateTime option
}

type Msg =
   | Set of string
   | Submit

let init () =
   {
      Input = ""
      ValidationError = None
      ValidateOnKeyPress = false
      DateSubmission = None
   },
   Cmd.none

let update
   (restrictDateToFuture: bool)
   (onValidDate: string * DateTime -> unit)
   msg
   state
   =
   let applyFutureDateValidator (formatted, date) =
      dateInFutureValidator "Expiration" date
      |> Result.map (fun _ -> formatted, date)

   let validation =
      Interpreter.validateInput Interpreter.DateSignifier.Single
      >> Result.bind (
         if restrictDateToFuture then
            applyFutureDateValidator
         else
            Ok
      )
      >> validationErrorsHumanFriendly

   match msg with
   | Msg.Set input ->
      let state = { state with Input = input }

      let state =
         if state.ValidateOnKeyPress then
            match validation input with
            | Ok _ -> { state with ValidationError = None }
            | Error errMsg -> {
               state with
                  ValidationError = Some errMsg
              }
         else
            state

      state, Cmd.none
   | Msg.Submit ->
      match validation state.Input with
      | Ok(formatted, date) ->
         let oldState = state

         let state = { state with Input = formatted }

         let state =
            match state.ValidationError with
            | None -> {
               state with
                  DateSubmission = Some date
              }
            | _ -> state

         if state.DateSubmission <> oldState.DateSubmission then
            onValidDate (formatted, date)

         state, Cmd.none
      | Error errMsg ->
         {
            state with
               Input = state.Input
               ValidationError = Some errMsg
               ValidateOnKeyPress = true
         },
         Cmd.none

[<ReactComponent>]
let DateInputComponent
   (props:
      {|
         OnValidDate: string * DateTime -> unit
         RestrictDateToFuture: bool
      |})
   =
   let state, dispatch =
      React.useElmish (
         init,
         update props.RestrictDateToFuture props.OnValidDate,
         [||]
      )

   React.fragment [
      Html.input [
         attr.type' "text"
         attr.value state.Input
         attr.placeholder
            $"Sep, Sep 17, 9/17, 9-17, 9 17 {DateTime.Now.Year}, etc."
         attr.onChange (Msg.Set >> dispatch)

         attr.onBlur (fun _ ->
            if state.ValidationError.IsNone then
               dispatch Msg.Submit)

         if state.ValidationError.IsSome then
            attr.ariaInvalid true
      ]

      match state.ValidationError with
      | Some errMsg -> Html.small [ attr.text errMsg ]
      | None -> ()
   ]

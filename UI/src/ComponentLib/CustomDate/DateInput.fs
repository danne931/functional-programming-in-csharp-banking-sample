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

let init initialInput () =
   {
      Input = initialInput
      ValidationError = None
      ValidateOnKeyPress = false
      DateSubmission = None
   },
   Cmd.none

let update (onValidDate: string * DateTime -> unit) msg state =
   let validation = Interpreter.validate Interpreter.DateSignifier.Single

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
         InitialInput: string
         OnValidDate: string * DateTime -> unit
         ExternalError: string option
      |})
   =
   let state, dispatch =
      React.useElmish (init props.InitialInput, update props.OnValidDate, [||])

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

         if props.ExternalError.IsSome || state.ValidationError.IsSome then
            attr.ariaInvalid true
      ]

      match props.ExternalError, state.ValidationError with
      | _, Some errMsg
      | Some errMsg, _ -> Html.small [ attr.text errMsg ]
      | _ -> ()
   ]

[<AutoOpen>]
module Util

open Feliz
open Fable.Core.JS
open System

module DateTime =
   let toISOString (date: DateTime) : string =
      Fable.Core.JsInterop.emitJsExpr (string date) "new Date($0).toISOString()"

   let rangeAsQueryString (startDate: DateTime) (endDate: DateTime) : string =
      toISOString startDate + "," + toISOString endDate

module Money =
   let format (amount: decimal) : string =
      Fable.Core.JsInterop.emitJsExpr
         amount
         """
         new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD'
         })
         .format($0)
         """

/// TLDR: This node has class.
/// Utility to create a node with classes and child nodes.
/// Reduces code nesting for the common use case of creating
/// wrapper nodes that only include a class attribute.
let classyNode
   (elementGenerator: IReactProperty list -> Fable.React.ReactElement)
   (classes: string seq)
   (children: Fable.React.ReactElement list)
   =
   elementGenerator [ attr.classes classes; attr.children children ]

/// Pass latest value from input, after some delay, to provided function.
let throttleUncontrolledInput (delay: int) (func: string -> unit) =
   let mutable timeoutId = None
   let mutable state = ""

   fun (input: string) ->
      state <- input

      if timeoutId.IsNone then
         let onTimer () =
            func state
            clearTimeout timeoutId.Value
            timeoutId <- None

         timeoutId <- Some <| setTimeout onTimer delay

/// Prevents user from typing any character other than a positive
/// number or backspace.
/// Useful in input onKeyDown handler.
let keepPositiveNumbers (e: Browser.Types.KeyboardEvent) : unit =
   if
      e.which = 8 // backspace
      || (e.which >= 48 && e.which <= 57) // number
   then
      ()
   else
      e.preventDefault ()

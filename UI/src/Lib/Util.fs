[<AutoOpen>]
module Util

open System
open Feliz

module time =
   let formatDate (date: DateTime) =
      let dayAndMonth = date.ToLongDateString().Split(string date.Year)[0]
      $"{dayAndMonth} {date.ToShortTimeString()}"

module money =
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
/// Useful in input onChange handler.
let throttleInput (delay: int) (func: string -> unit) =
   let mutable timeoutId = None
   let mutable state = ""

   fun (input: string) ->
      state <- input

      if timeoutId.IsNone then
         timeoutId <-
            Some
            <| Browser.Dom.window.setTimeout (
               fun () ->
                  func state
                  timeoutId <- None
               , delay
            )

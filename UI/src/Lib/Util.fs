[<AutoOpen>]
module Util

open System
open Feliz

module time =
   let formatDate (date: DateTime) =
      $"{date.ToLongDateString()} {date.ToShortTimeString()}"

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

module LeaderLine

open Fable.Core.JsInterop
open Browser.Types

type ILeaderLine =
   abstract remove: unit -> unit
   abstract setOptions: obj -> unit

let private leaderLine: obj = importDefault "LeaderLine"

[<RequireQualifiedAccess>]
type ArrowDirection =
   | StartToEnd
   | Bidirectional

let private plug = "arrow3"

let private lineOpts = {|
   dash = {| animation = false |}
   color = "#afbbc4"
|}

let private activeLineOpts = {|
   dash = {| animation = true |}
   color = "#388e3c"
|}

let create
   (startEl: Element)
   (endEl: Element)
   (direction: ArrowDirection)
   : ILeaderLine
   =
   let opts = {|
      startPlug =
         match direction with
         | ArrowDirection.Bidirectional -> plug
         | ArrowDirection.StartToEnd -> null
      endPlug = plug
      path = "magnet"
      size = 3
      dropShadow = true
      dash = lineOpts.dash
      color = lineOpts.color
   |}

   createNew leaderLine (startEl, endEl, opts) :?> ILeaderLine

let animate (line: ILeaderLine) : ILeaderLine =
   line.setOptions activeLineOpts
   line

let stopAnimation (line: ILeaderLine) : ILeaderLine =
   line.setOptions lineOpts
   line

let remove (line: ILeaderLine) : unit = line.remove ()

[<AutoOpen>]
module ElmishExtensions

open Elmish

module Cmd =
   let fromAsync (operation: Async<'Msg>) : Cmd<'Msg> =
      let asyncCmd (dispatch: 'Msg -> unit) : unit =
         let asyncDispatch = async {
            let! msg = operation
            dispatch msg
         }

         Async.StartImmediate asyncDispatch

      Cmd.ofEffect asyncCmd

   let fromTimeout (milliseconds: int) (msg: 'Msg) =
      let operation = async {
         do! Async.Sleep milliseconds
         return msg
      }

      fromAsync operation

   let indefinite (timeout: int) (msg: 'Msg) =
      let asyncCmd (dispatch: 'Msg -> unit) : unit =
         let asyncDispatch = async {
            while true do
               do! Async.Sleep timeout
               dispatch msg
         }

         Async.StartImmediate asyncDispatch

      Cmd.ofEffect asyncCmd

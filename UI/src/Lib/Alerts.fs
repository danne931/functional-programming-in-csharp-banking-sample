[<RequireQualifiedAccess>]
module Alerts

open Elmish.SweetAlert

open Lib.SharedTypes

let toast (err: Err) =
   ToastAlert(err.HumanFriendly)
      .Title("Error:")
      .Position(AlertPosition.Top)
      .Type(AlertType.Error)

let toastCommand (err: Err) : Elmish.Cmd<_> = toast err |> SweetAlert.Run

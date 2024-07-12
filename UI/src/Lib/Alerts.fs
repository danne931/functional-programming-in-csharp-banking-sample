[<RequireQualifiedAccess>]
module Alerts

open Elmish.SweetAlert

open Lib.SharedTypes

let toast (err: Err) =
   ToastAlert(err.HumanFriendly)
      .Title("Error:")
      .Position(AlertPosition.Bottom)
      .Type(AlertType.Error)

let toastCommand (err: Err) : Elmish.Cmd<_> = toast err |> SweetAlert.Run

let toastSuccess (msg: string) =
   ToastAlert(msg)
      .Title("Success:")
      .Timeout(6000)
      .Position(AlertPosition.Bottom)
      .Type(AlertType.Success)

let toastSuccessCommand (msg: string) : Elmish.Cmd<_> =
   toastSuccess msg |> SweetAlert.Run

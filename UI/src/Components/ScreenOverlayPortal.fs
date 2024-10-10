[<RequireQualifiedAccess>]
module ScreenOverlay

open Feliz
open Browser.Dom
open Fable.Core.JsInterop

open UIDomain.Employee

let getEl () =
   document.getElementById "screen-overlay-portal"

let Portal (content: ReactElement) =
   ReactDOM.createPortal (content, getEl ())

type OverlaySize =
   | Standard
   | Wide

let manageVisibility (url: Routes.IndexUrl) =
   let overlay =
      match url with
      | Routes.IndexUrl.Cards url ->
         match url with
         | Routes.CardUrl.CardsWithSearchQuery query ->
            query.Action |> Option.map (fun _ -> OverlaySize.Standard)
         | _ -> None
      | Routes.IndexUrl.Employees url ->
         match url with
         | Routes.EmployeeUrl.EmployeesWithSearchQuery query ->
            query.Action
            |> Option.map (function
               | EmployeeActionView.Create -> OverlaySize.Wide
               | EmployeeActionView.ViewEmployee _ -> OverlaySize.Standard)
         | _ -> None
      | Routes.IndexUrl.Transaction url ->
         match url with
         | Routes.TransactionUrl.AccountSelectedWithQuery(_, browserQuery) ->
            if
               browserQuery.Action.IsSome || browserQuery.Transaction.IsSome
            then
               Some OverlaySize.Standard
            else
               None
         | _ -> None
      | Routes.IndexUrl.Account url ->
         match url with
         | Routes.AccountUrl.CreateAccount -> Some OverlaySize.Standard
         | Routes.AccountUrl.CreateRule _
         | Routes.AccountUrl.EditRule _ -> Some OverlaySize.Wide
         | _ -> None
      | Routes.IndexUrl.Payments url ->
         match url with
         | Routes.PaymentUrl.RequestPayment
         | Routes.PaymentUrl.ViewPayment _ -> Some OverlaySize.Standard
         | _ -> None
      | _ -> None

   let overlayEl = getEl().closest ".screen-overlay"

   match overlayEl with
   | None -> Log.error "Unable to activate screen-overlay."
   | Some overlayEl ->
      match overlay with
      | Some overlay ->
         overlayEl.classList.add "active"
         document.body?style?overflow <- "hidden"

         let childGridEl = overlayEl.querySelector ".grid"

         match overlay with
         | OverlaySize.Wide -> childGridEl.classList.add "overlay-wide"
         | OverlaySize.Standard -> childGridEl.classList.remove "overlay-wide"
      | None ->
         overlayEl.classList.remove "active"
         document.body?style?overflow <- "scroll"

module App

open Fable.Core.JsInterop
importSideEffects "./Styles/site.css"

open Feliz
open Feliz.Router
open Browser.Dom
open SignalRConnectionProvider

[<RequireQualifiedAccess>]
type Url =
   | Index
   | Account of AccountDashboard.Url
   | NotFound

let parseUrl (segments: string list) =
   // Temporarily redirect Index page to Accounts.
   let segments = if segments.IsEmpty then [ "account" ] else segments

   match segments with
   | [] -> Url.Index
   // Matches /account/{AccountDashboard.Url}
   | "account" :: accountSegments ->
      Url.Account(AccountDashboard.Url.parse accountSegments)
   | _ -> Url.NotFound

[<ReactComponent>]
let App () =
   let currentUrl, updateUrl = React.useState (parseUrl <| Router.currentUrl ())

   let activePage =
      match currentUrl with
      | Url.Index -> Html.h1 "Home"
      | Url.Account url ->
         SignalRConnectionProvider(
            AccountDashboard.AccountDashboardComponent url
         )
      | Url.NotFound -> Html.h1 "Not Found"

   Html.div [
      React.router [
         router.onUrlChanged (parseUrl >> updateUrl)
         router.children [ activePage ]
      ]
   ]

let root = ReactDOM.createRoot <| document.getElementById "bank-react-root"
root.render <| App()

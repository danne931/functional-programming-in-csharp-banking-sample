module App

open Fable.Core.JsInterop
importSideEffects "./Styles/site.css"

open Feliz
open Feliz.Router
open Browser.Dom
open SignalRConnectionProvider
open TransactionCategoryProvider
open MerchantProvider
open Lib.SharedTypes

[<ReactComponent>]
let App () =
   let currentUrl, setUrl =
      React.useState (Routes.IndexUrl.parse <| Router.currentUrl ())

   let activePage =
      match currentUrl with
      | Routes.IndexUrl.Reporting -> Html.h1 "Reporting"
      | Routes.IndexUrl.Account url ->
         AccountDashboard.AccountDashboardComponent url
         |> SignalRConnectionProvider
         |> TransactionCategoryProvider
         // TODO: Get OrgId from user session.
         |> MerchantProvider ORG_ID_REMOVE_SOON
      | Routes.IndexUrl.NotFound -> Html.h1 "Not Found"

   React.strictMode (
      [
         Html.div [
            React.router [
               router.onUrlChanged (Routes.IndexUrl.parse >> setUrl)
               router.children [ activePage ]
            ]
         ]
      ]
   )

let root = ReactDOM.createRoot <| document.getElementById "bank-react-root"
root.render <| App()

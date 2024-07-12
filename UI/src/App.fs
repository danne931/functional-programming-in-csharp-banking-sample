module App

open Fable.Core.JsInterop
importSideEffects "./Styles/site.sass"

open Feliz
open Feliz.Router
open Browser.Dom

open SignalRConnectionProvider
open SignalRAccountEventProvider
open TransactionCategoryProvider
open UserSessionProvider
open MerchantProvider
open OrgAndAccountProfileProvider

[<ReactComponent>]
let App () =
   let currentUrl, setUrl =
      React.useState (Routes.IndexUrl.parse <| Router.currentUrl ())

   React.useEffect (
      fun () -> ScreenOverlay.manageVisibility currentUrl
      , [| box (string currentUrl) |]
   )

   let activePage =
      match currentUrl with
      | Routes.IndexUrl.Reporting -> Html.h1 "Reporting"
      | Routes.IndexUrl.Employees url ->
         EmployeeDashboard.EmployeeDashboardComponent url
         |> UserSessionSuspense
         |> (UserSessionProvider << OrgAndAccountProfileProvider)
      | Routes.IndexUrl.EmployeeHistory url ->
         EmployeeHistoryDashboard.EmployeeHistoryDashboardComponent url
         |> UserSessionSuspense
         |> UserSessionProvider
      | Routes.IndexUrl.Cards url ->
         CardDashboard.CardDashboardComponent url
         |> UserSessionSuspense
         |> UserSessionProvider
      | Routes.IndexUrl.Account url ->
         AccountDashboard.AccountDashboardComponent url
         |> UserSessionSuspense
         |> (UserSessionProvider
             << OrgAndAccountProfileProvider
             << SignalRConnectionProvider
             << SignalRAccountEventProvider
             << MerchantProvider
             << TransactionCategoryProvider)
      | Routes.IndexUrl.NotFound -> Html.h1 "Not Found"

   [
      Navigation.element

      Html.div [
         React.router [
            router.onUrlChanged (Routes.IndexUrl.parse >> setUrl)
            router.children [
               classyNode Html.div [ "container-fluid"; "app-shell" ] [
                  classyNode Html.div [ "grid" ] [
                     classyNode Html.aside [ "menu" ] [
                        SidebarMenu.render currentUrl
                        |> UserSessionSuspense
                        |> UserSessionProvider
                     ]

                     Html.section [ activePage ]
                  ]
               ]
            ]
         ]
      ]
   ]
   |> React.strictMode

let root = ReactDOM.createRoot <| document.getElementById "bank-react-root"
root.render <| App()

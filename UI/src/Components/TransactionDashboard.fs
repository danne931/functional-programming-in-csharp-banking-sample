module TransactionDashboard

open Feliz
open Feliz.Router

open Bank.Account.Domain
open Bank.Employee.Domain
open UIDomain.Account
open TransactionDetail
open AccountSelection

[<ReactComponent>]
let TransactionDashboardComponent
   (url: Routes.TransactionUrl)
   (session: UserSession)
   =
   let orgCtx = React.useContext OrgProvider.context
   let accountIdOpt = Routes.TransactionUrl.accountIdMaybe url

   // Redirect /account to /account/{first-account-id}
   React.useEffect (
      fun () ->
         match orgCtx, url with
         | Deferred.Resolved(Ok(Some org)), Routes.TransactionUrl.Account ->
            org.Accounts
            |> Map.values
            |> Seq.head
            |> _.AccountId
            |> Routes.TransactionUrl.selectedPath
            |> Router.navigate
         | _ -> ()
      , [| box orgCtx; box (string url) |]
   )

   let accountOpt =
      match accountIdOpt, orgCtx with
      | Some accountId, Deferred.Resolved(Ok(Some org)) ->
         org.Accounts |> Map.tryFind accountId
      | _ -> None

   classyNode Html.div [ "transaction-dashboard" ] [
      match orgCtx with
      | Deferred.Resolved(Ok(Some org)) ->
         AccountSelectionComponent accountIdOpt org.Accounts
         |> Navigation.Portal
      | _ -> ()

      ServiceHealth.ServiceHealthComponent()

      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [
               Html.h4 "Transactions"

               match accountOpt with
               | Some account ->
                  TransactionTable.TransactionTableComponent account session
               | _ -> Html.progress []
            ]

            match
               orgCtx, accountOpt, Routes.IndexUrl.accountBrowserQuery().Action
            with
            | Deferred.Resolved(Ok(Some org)), Some account, Some action ->
               AccountActions.AccountActionsComponent
                  session
                  account
                  org.Accounts
                  action
               |> ScreenOverlay.Portal
            | _, Some account, _ ->
               Html.aside [ AccountActionMenu.render account ]
            | _ -> ()

            match Routes.TransactionUrl.transactionIdMaybe url with
            | Some txnId ->
               match accountOpt with
               | Some account ->
                  TransactionDetailComponent session account txnId
               | _ -> Html.progress []
               |> ScreenOverlay.Portal
            | None -> ()
         ]
      ]

      match orgCtx, accountIdOpt with
      | Deferred.Resolved(Ok(Some org)), Some accountId ->
         match org.AccountProfiles.TryFind accountId with
         | Some profile -> AccountSummary.AccountSummaryComponent profile
         | None -> ()
      | _ -> ()
   ]

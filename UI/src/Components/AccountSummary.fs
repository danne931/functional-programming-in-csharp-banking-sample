module AccountSummary

open Feliz
open Fable.Core.JS
open Feliz.Router

open Bank.Account.Domain

[<RequireQualifiedAccess>]
type private RotatingMetric =
   | DailyInternalTransfer
   | DailyDomesticTransfer

[<ReactComponent>]
let AccountSummaryComponent (account: AccountProfile) =
   let isFading, setFading = React.useState false
   let flow, setFlow = React.useState MoneyFlow.Out

   let rotatingMetric, setMetric =
      React.useState RotatingMetric.DailyInternalTransfer

   let monthlyMoneyFlow, setMonthlyMoneyFlow =
      React.useState Deferred.InProgress

   let changeDetection = box (string account.AccountId)

   React.useEffect (
      fun () ->
         async {
            let! res =
               AnalyticsService.loadMoneyFlowMonthlyTimeSeriesForAccount
                  account.AccountId

            setMonthlyMoneyFlow (Deferred.Resolved res)
         }
         |> Async.StartImmediate
      , [| changeDetection |]
   )

   React.useEffect (
      fun () ->
         let mutable flow = flow

         let interval =
            setInterval
               (fun () ->
                  let updatedFlow =
                     if flow = MoneyFlow.In then
                        MoneyFlow.Out
                     else
                        MoneyFlow.In

                  flow <- updatedFlow
                  setFlow flow)
               10000

         React.createDisposable (fun _ -> clearInterval interval)
      , [||]
   )

   React.useEffect (
      fun () ->
         let mutable rotatingMetric = rotatingMetric
         let mutable isFading = isFading
         let mutable timer = 0

         let setFading fading =
            isFading <- fading
            setFading fading

         let setMetric metric =
            rotatingMetric <- metric
            setMetric metric

         let rotate () =
            let metric =
               if rotatingMetric = RotatingMetric.DailyInternalTransfer then
                  RotatingMetric.DailyDomesticTransfer
               else
                  RotatingMetric.DailyInternalTransfer

            setMetric metric
            setFading false

         let interval =
            setInterval
               (fun () ->
                  setFading true
                  timer <- setTimeout rotate 1000)
               5000

         React.createDisposable (fun _ ->
            clearTimeout timer
            clearInterval interval)
      , [||]
   )

   Html.footer [
      attr.classes [ "container-fluid grid" ]

      attr.children [
         Html.div [
            Html.b [ attr.classes [ "account-name" ]; attr.text account.Name ]

            Html.div [
               Html.p "Balance: "
               Html.ins [ attr.text (Money.format account.Balance) ]
            ]
         ]

         Html.div [
            classyNode Html.div [
               "rotating-footer-metric"
               if isFading then "fade-out" else "fade-in"
            ] [
               match rotatingMetric with
               | RotatingMetric.DailyInternalTransfer ->
                  Html.p "Daily Internal Transfer: "

                  Html.ins [
                     attr.text (
                        Money.formatShort account.DailyInternalTransferAccrued
                     )
                  ]
               | RotatingMetric.DailyDomesticTransfer ->
                  Html.p "Daily Domestic Transfer: "

                  Html.ins [
                     attr.text (
                        Money.formatShort account.DailyDomesticTransferAccrued
                     )
                  ]
            ]
         ]

         match monthlyMoneyFlow with
         | Deferred.Resolved(Ok(Some analytics)) ->
            Html.div [
               AnalyticsDashboard.render3MonthTimeSeriesChart
                  flow
                  analytics.TimeSeries
                  {| Height = 70; Width = 120 |}

               Html.a [
                  attr.href ""
                  attr.text "View Analytics"
                  attr.onClick (fun e ->
                     e.preventDefault ()
                     Router.navigate Routes.AnalyticsUrl.BasePath)
               ]
            ]
         | _ -> ()
      ]
   ]

module AnalyticsDashboard

open Feliz
open Feliz.UseElmish
open Elmish
open Feliz.Router
open Fable.FontAwesome
open Feliz.Recharts
open System

open Bank.Account.Domain
open Bank.Employee.Domain
open Lib.SharedTypes
open Lib.Time
open UIDomain
open UIDomain.Account

// NOTE:
// Modifies Feliz.Recharts to support the yAxis tickFormatter property included in Recharts.
type yAxis =
   static member inline tickFormatter(f: int -> string) =
      Interop.mkYAxisAttr "tickFormatter" f

[<RequireQualifiedAccess>]
type SelectedTimeSeriesChart =
   | TimeSeries
   | TimeSeriesByAccount

[<RequireQualifiedAccess>]
type SelectedTimeSeriesYAxis =
   | All
   | Balance
   | MoneyIn
   | MoneyOut

type State = {
   SelectedTimeSeriesChart: SelectedTimeSeriesChart
   SelectedTimeSeriesDateFilter: DateFilter
   SelectedTimeSeriesYAxis: SelectedTimeSeriesYAxis
   SelectedTopNMonth: DateTime
   SelectedTopNPurchasersMonth: DateTime
   Analytics: Deferred<Result<MoneyFlowAnalytics, Err>>
   TimeSeriesUpdateInProgress: bool
   TopNUpdateInProgress: bool
   TopNPurchasersUpdateInProgress: bool
}

type Msg =
   | LoadInitialAnalytics of
      AsyncOperationStatus<Result<MoneyFlowAnalytics, Err>>
   | ResolvedTimeSeriesFilterUpdate of
      Result<MoneyFlowDailyTimeSeriesAnalytics option, Err>
   | ResolvedTopNFilterUpdate of Result<MoneyFlowTopNAnalytics option, Err>
   | ResolvedTopNPurchasersFilterUpdate of
      Result<EmployeePurchaserTopN list option, Err>
   | SelectTimeSeriesChart of SelectedTimeSeriesChart
   | SelectTimeSeriesYAxisFilter of SelectedTimeSeriesYAxis
   | SelectDateFilter of DateFilter
   | SelectTopNMonth of DateTime
   | SelectTopNPurchasersMonth of DateTime

let init () =
   {
      SelectedTopNMonth = DateTime.UtcNow
      SelectedTopNPurchasersMonth = DateTime.UtcNow
      SelectedTimeSeriesChart = SelectedTimeSeriesChart.TimeSeries
      SelectedTimeSeriesDateFilter = DateFilter.Last30Days
      SelectedTimeSeriesYAxis = SelectedTimeSeriesYAxis.All
      Analytics = Deferred.Idle
      TimeSeriesUpdateInProgress = false
      TopNUpdateInProgress = false
      TopNPurchasersUpdateInProgress = false
   },
   Cmd.ofMsg (LoadInitialAnalytics Started)

let update (session: UserSession) msg state =
   match msg with
   | SelectTimeSeriesChart c ->
      {
         state with
            SelectedTimeSeriesChart = c
      },
      Cmd.none
   | SelectTimeSeriesYAxisFilter filter ->
      {
         state with
            SelectedTimeSeriesYAxis = filter
      },
      Cmd.none
   | SelectDateFilter f ->
      let load = async {
         let! res = AnalyticsService.loadTimeSeriesAnalytics session.OrgId f
         return Msg.ResolvedTimeSeriesFilterUpdate res
      }

      {
         state with
            SelectedTimeSeriesDateFilter = f
            TimeSeriesUpdateInProgress = true
      },
      Cmd.fromAsync load
   | SelectTopNMonth date ->
      let load = async {
         let! res = AnalyticsService.loadTopNAnalytics session.OrgId date
         return Msg.ResolvedTopNFilterUpdate res
      }

      {
         state with
            SelectedTopNMonth = date
            TopNUpdateInProgress = true
      },
      Cmd.fromAsync load
   | SelectTopNPurchasersMonth date ->
      let load = async {
         let! res =
            AnalyticsService.loadTopNPurchasersAnalytics session.OrgId date

         return Msg.ResolvedTopNPurchasersFilterUpdate res
      }

      {
         state with
            SelectedTopNPurchasersMonth = date
            TopNPurchasersUpdateInProgress = true
      },
      Cmd.fromAsync load
   | ResolvedTimeSeriesFilterUpdate(Ok series) ->
      {
         state with
            Analytics =
               state.Analytics
               |> (Deferred.map << Result.map) (fun analytics -> {
                  analytics with
                     TimeSeriesDaily = series
               })
            TimeSeriesUpdateInProgress = false
      },
      Cmd.none
   | ResolvedTimeSeriesFilterUpdate(Error err) ->
      Log.error $"Error updating time series filter {err}"
      state, Cmd.none
   | ResolvedTopNFilterUpdate(Ok topN) ->
      {
         state with
            Analytics =
               state.Analytics
               |> (Deferred.map << Result.map) (fun analytics -> {
                  analytics with
                     TopN = topN
               })
            TopNUpdateInProgress = false
      },
      Cmd.none
   | ResolvedTopNFilterUpdate(Error err) ->
      Log.error $"Error updating topN money flow filter {err}"
      state, Cmd.none
   | ResolvedTopNPurchasersFilterUpdate(Ok topN) ->
      {
         state with
            Analytics =
               state.Analytics
               |> (Deferred.map << Result.map) (fun analytics -> {
                  analytics with
                     TopNPurchasers = topN
               })
            TopNPurchasersUpdateInProgress = false
      },
      Cmd.none
   | ResolvedTopNPurchasersFilterUpdate(Error err) ->
      Log.error $"Error updating topN purchasers filter {err}"
      state, Cmd.none
   | LoadInitialAnalytics Started ->
      let load = async {
         let! res =
            AnalyticsService.loadInitialAnalytics
               session.OrgId
               state.SelectedTimeSeriesDateFilter

         return LoadInitialAnalytics(Finished res)
      }

      {
         state with
            Analytics = Deferred.InProgress
      },
      Cmd.fromAsync load
   | LoadInitialAnalytics(Finished(Ok analytics)) ->
      {
         state with
            Analytics = Deferred.Resolved(Ok analytics)
      },
      Cmd.none
   | LoadInitialAnalytics(Finished(Error err)) ->
      {
         state with
            Analytics = Deferred.Resolved(Error err)
      },
      Cmd.none

let createGradient (id: string) color =
   Svg.linearGradient [
      svg.id id
      svg.x1 0
      svg.x2 0
      svg.y1 0
      svg.y2 1
      svg.children [
         Svg.stop [
            svg.offset (length.percent 5)
            svg.stopColor color
            svg.stopOpacity 0.8
         ]
         Svg.stop [
            svg.offset (length.percent 95)
            svg.stopColor color
            svg.stopOpacity 0.2
         ]
      ]
   ]

let private opts = {|
   ResponsiveContainer = {|
      Width = length.percent 100
      Height = 400
   |}
   Color = {|
      MoneyIn = "#388e3c"
      MoneyOut = "#c62828"
      Balance = "#fdd835"
   |}
   ChartMarginHorizontal = 30
   AmountInterval = 5
   YAxisPaddingBottom = 20
|}

let renderTimeSeriesChart
   (dateFilter: DateFilter)
   (yAxisFilter: SelectedTimeSeriesYAxis)
   (children: ReactElement list)
   =
   Recharts.lineChart [
      lineChart.margin (
         left = opts.ChartMarginHorizontal,
         right = opts.ChartMarginHorizontal
      )

      lineChart.children [
         Recharts.xAxis [
            xAxis.allowDuplicatedCategory false
            xAxis.interval (
               match dateFilter with
               | DateFilter.LastYear
               | DateFilter.CurrentYear -> 30
               | _ -> 5
            )
            xAxis.dataKey (_.Day >> DateTime.formatShort)
         ]

         if yAxisFilter <> SelectedTimeSeriesYAxis.Balance then
            Recharts.yAxis [
               yAxis.yAxisId "moneyFlow"
               yAxis.padding (bottom = opts.YAxisPaddingBottom)
               yAxis.tickCount opts.AmountInterval
               yAxis.tickFormatter (decimal >> Money.formatShort)
            ]

         Recharts.yAxis [
            yAxis.yAxisId "balance"
            if yAxisFilter <> SelectedTimeSeriesYAxis.Balance then
               yAxis.orientation.right
            yAxis.padding (bottom = opts.YAxisPaddingBottom)
            yAxis.tickCount opts.AmountInterval
            yAxis.tickFormatter (decimal >> Money.formatShort)
         ]

         Recharts.tooltip [
            tooltip.formatter (fun value _ _ ->
               value
               |> decimal
               |> Money.format
               |> fun (m: string) -> m.Split "."
               |> Array.head)
         ]

         yield! children
      ]
   ]

let renderTimeSeriesByOrgChart
   (series: MoneyFlowDailyTimeSeriesByOrg list)
   (dateFilter: DateFilter)
   (yAxisFilter: SelectedTimeSeriesYAxis)
   =
   renderTimeSeriesChart dateFilter yAxisFilter [
      match yAxisFilter with
      | SelectedTimeSeriesYAxis.All
      | SelectedTimeSeriesYAxis.MoneyIn ->
         Recharts.line [
            line.yAxisId "moneyFlow"
            line.monotone
            line.data series
            line.dataKey (_.AmountIn >> int)
            line.stroke opts.Color.MoneyIn
            line.dot false
            line.name "Money In"
         ]
      | _ -> ()

      match yAxisFilter with
      | SelectedTimeSeriesYAxis.All
      | SelectedTimeSeriesYAxis.MoneyOut ->
         Recharts.line [
            line.yAxisId "moneyFlow"
            line.monotone
            line.data series
            line.dataKey (_.AmountOut >> int)
            line.stroke opts.Color.MoneyOut
            line.dot false
            line.name "Money Out"
         ]
      | _ -> ()

      match yAxisFilter with
      | SelectedTimeSeriesYAxis.All
      | SelectedTimeSeriesYAxis.Balance ->
         Recharts.line [
            line.yAxisId "balance"
            line.monotone
            line.data series
            line.dataKey (fun a -> int a.BalanceHistory.Balance)
            line.stroke opts.Color.Balance
            line.dot false
            line.name "Balance"
         ]
      | _ -> ()
   ]

let renderTimeSeriesByAccountChart
   (series: MoneyFlowDailyTimeSeriesByAccount list)
   (accounts: Map<AccountId, Account>)
   (dateFilter: DateFilter)
   (yAxisFilter: SelectedTimeSeriesYAxis)
   =
   let series = series |> List.groupBy _.AccountId

   renderTimeSeriesChart dateFilter yAxisFilter [
      for accountId, data in series do
         let name =
            accounts
            |> Map.tryFind accountId
            |> Option.map _.FullName
            |> Option.defaultValue (string accountId)

         React.keyedFragment (
            AccountId.get accountId,
            [
               match yAxisFilter with
               | SelectedTimeSeriesYAxis.All
               | SelectedTimeSeriesYAxis.MoneyIn ->
                  Recharts.line [
                     line.yAxisId "moneyFlow"
                     line.monotone
                     line.data data
                     line.dataKey (_.AmountIn >> int)
                     line.stroke opts.Color.MoneyIn
                     line.dot false
                     line.name name
                  ]
               | _ -> ()

               match yAxisFilter with
               | SelectedTimeSeriesYAxis.All
               | SelectedTimeSeriesYAxis.MoneyOut ->
                  Recharts.line [
                     line.yAxisId "moneyFlow"
                     line.monotone
                     line.data data
                     line.dataKey (_.AmountOut >> int)
                     line.stroke opts.Color.MoneyOut
                     line.dot false
                     line.name name
                  ]
               | _ -> ()

               match yAxisFilter with
               | SelectedTimeSeriesYAxis.All
               | SelectedTimeSeriesYAxis.Balance ->
                  Recharts.line [
                     line.yAxisId "balance"
                     line.monotone
                     line.data data
                     line.dataKey (fun a -> int a.BalanceHistory.Balance)
                     line.stroke opts.Color.Balance
                     line.dot false
                     line.name name
                  ]
               | _ -> ()
            ]
         )
   ]

let render3MonthTimeSeriesChart
   (flow: MoneyFlow)
   (data: MoneyFlowMonthlyTimeSeries list)
   (dimensions: {| Height: int; Width: int |})
   =
   let dataKey, color =
      match flow with
      | MoneyFlow.In -> (_.AmountIn >> string), "url(#moneyIn)"
      | MoneyFlow.Out -> (_.AmountOut >> string), "url(#moneyOut)"

   Recharts.barChart [
      barChart.width dimensions.Width
      barChart.height dimensions.Height
      barChart.data data

      barChart.children [
         Svg.defs [
            createGradient "moneyIn" opts.Color.MoneyIn
            createGradient "moneyOut" opts.Color.MoneyOut
         ]

         Recharts.tooltip [
            tooltip.position (150, -13)
            tooltip.formatter (fun value _ _ ->
               Money.formatShort (decimal value))
         ]

         Recharts.xAxis [
            xAxis.hide true
            xAxis.dataKey (fun (d: MoneyFlowMonthlyTimeSeries) ->
               DateTime.numberToDisplayMonth[d.Month.Month])
         ]

         Recharts.bar [ bar.dataKey dataKey; bar.fill color ]
      ]
   ]

let renderTimeSeriesChartSelect state dispatch =
   Html.select [
      attr.onChange (
         function
         | "TimeSeries" -> SelectedTimeSeriesChart.TimeSeries
         | _ -> SelectedTimeSeriesChart.TimeSeriesByAccount
         >> Msg.SelectTimeSeriesChart
         >> dispatch
      )
      attr.value (string state.SelectedTimeSeriesChart)

      attr.children [
         Html.option [
            attr.value (string SelectedTimeSeriesChart.TimeSeries)
            attr.text "Time Series"
         ]

         Html.option [
            attr.value (string SelectedTimeSeriesChart.TimeSeriesByAccount)
            attr.text "Time Series by Account"
         ]
      ]
   ]

let renderTimeSeriesYAxisSelect state dispatch =
   Html.select [
      attr.onChange (
         function
         | "Balance" -> SelectedTimeSeriesYAxis.Balance
         | "MoneyIn" -> SelectedTimeSeriesYAxis.MoneyIn
         | "MoneyOut" -> SelectedTimeSeriesYAxis.MoneyOut
         | _ -> SelectedTimeSeriesYAxis.All
         >> Msg.SelectTimeSeriesYAxisFilter
         >> dispatch
      )
      attr.value (string state.SelectedTimeSeriesYAxis)

      attr.children [
         Html.option [
            attr.value (string SelectedTimeSeriesYAxis.All)
            attr.text "All"
         ]

         Html.option [
            attr.value (string SelectedTimeSeriesYAxis.Balance)
            attr.text "Balance"
         ]

         Html.option [
            attr.value (string SelectedTimeSeriesYAxis.MoneyOut)
            attr.text "Money Out"
         ]

         Html.option [
            attr.value (string SelectedTimeSeriesYAxis.MoneyIn)
            attr.text "Money In"
         ]
      ]
   ]

let renderTopNMonthSelect
   (selectedMonth: DateTime)
   (onSelect: DateTime -> unit)
   =
   let currDate = DateTime.UtcNow
   let prevDate = currDate.AddMonths -1
   let prevPrevDate = currDate.AddMonths -2

   let dateMap =
      Map [
         currDate.Month, currDate
         prevDate.Month, prevDate
         prevPrevDate.Month, prevPrevDate
      ]

   Html.select [
      attr.value selectedMonth.Month

      attr.onChange (fun (month: string) -> onSelect dateMap[int month])

      attr.children [
         for month, opt in Map.toSeq dateMap do
            Html.option [
               attr.value month
               attr.text $"{DateTime.numberToDisplayMonth[month]} {opt.Year}"
            ]
      ]
   ]

let txnsButton
   (flow: MoneyFlow)
   (accounts: Map<AccountId, Account>)
   (selectedTopNMonth: DateTime)
   =
   let start = DateTime(selectedTopNMonth.Year, selectedTopNMonth.Month, 1)
   let filter = DateFilter.Custom(start, start.AddMonths(1).AddDays(-1))

   Html.button [
      attr.classes [ "outline" ]
      attr.children [ Fa.i [ Fa.Solid.History ] []; Html.span "View all" ]

      attr.onClick (fun _ ->
         let pathArr =
            Routes.TransactionUrl.selectedPath (Seq.head accounts.Keys)

         let queryString =
            {
               AccountBrowserQuery.empty with
                  MoneyFlow = Some flow
                  Date = Some filter
            }
            |> AccountBrowserQuery.toQueryParams
            |> Router.encodeQueryString

         Router.navigate [| yield! pathArr; queryString |])
   ]

let renderTopMoneyListItems
   (flow: MoneyFlow)
   (accounts: Map<AccountId, Account>)
   (topN: MoneyFlowTopNAnalytics)
   (last3MonthTimeSeries: MoneyFlowMonthlyTimeSeriesAnalytics)
   (selectedMonth: DateTime)
   =
   let selected =
      last3MonthTimeSeries.TimeSeries
      |> List.find (fun a -> a.Month.Month = selectedMonth.Month)

   let str, color, items, totalAmount =
      match flow with
      | MoneyFlow.In -> "in", "credit", topN.In, selected.AmountIn
      | MoneyFlow.Out -> "out", "debit", topN.Out, selected.AmountOut

   let renderAmount (amount: decimal) =
      Html.p [ attr.classes [ color ]; attr.text (Money.format amount) ]

   Html.div [
      classyNode Html.div [ "top-n-list-item" ] [
         Html.b $"Money {str}"
         renderAmount totalAmount
      ]

      Html.hr []
      Html.br []

      if items.Length = 0 then
         Html.p $"No money {str} for this period."
      else
         classyNode Html.div [ "top-n-list-container" ] [
            for item in items do
               Html.div [ Html.b item.Source; renderAmount item.Amount ]
         ]

         txnsButton flow accounts selectedMonth

         Html.hr []
         Html.br []

         classyNode Html.div [ "analytics-monthly-time-series"; "grid" ] [
            Html.div [
               Html.p [
                  attr.style [ style.marginBottom 0 ]
                  attr.text "3 month average"
               ]

               match flow with
               | MoneyFlow.In -> renderAmount last3MonthTimeSeries.AverageIn
               | MoneyFlow.Out -> renderAmount last3MonthTimeSeries.AverageOut
            ]

            render3MonthTimeSeriesChart flow last3MonthTimeSeries.TimeSeries {|
               Height = 90
               Width = 150
            |}
         ]
   ]

let renderTopPurchasers (topN: EmployeePurchaserTopN list) =
   let renderAmount (amount: decimal) =
      Html.p [ attr.classes [ "debit" ]; attr.text (Money.format amount) ]

   Html.div [
      classyNode Html.div [ "top-n-list-container" ] [
         for purchaser in topN do
            Html.div [
               Html.b purchaser.EmployeeName
               renderAmount purchaser.Amount
            ]
      ]


      Html.button [
         attr.classes [ "outline" ]
         attr.children [ Fa.i [ Fa.Solid.History ] []; Html.span "View all" ]
         attr.onClick (fun _ -> Router.navigate Routes.TransactionUrl.BasePath)
      ]
   ]

[<ReactComponent>]
let AnalyticsDashboardComponent
   (url: Routes.AnalyticsUrl)
   (session: UserSession)
   =
   let state, dispatch = React.useElmish (init, update session, [||])
   let orgCtx = React.useContext OrgProvider.context

   classyNode Html.div [ "analytics-dashboard" ] [
      classyNode Html.main [ "container-fluid" ] [
         match state.Analytics, orgCtx with
         | Deferred.Resolved(Ok analytics), Deferred.Resolved(Ok(Some org)) ->
            classyNode Html.div [ "analytics-chart-filter"; "grid" ] [
               renderTimeSeriesChartSelect state dispatch

               DateFilter.renderDateFilterSelect
                  (Some state.SelectedTimeSeriesDateFilter)
                  (Option.defaultValue DateFilter.Last30Days
                   >> Msg.SelectDateFilter
                   >> dispatch)
                  false

               renderTimeSeriesYAxisSelect state dispatch
            ]

            Html.progress [
               if not state.TimeSeriesUpdateInProgress then
                  attr.value 100

               attr.style [ style.marginBottom 25 ]
            ]

            match analytics.TimeSeriesDaily with
            | None -> Html.p "No data for this period."
            | Some timeSeries ->
               Recharts.responsiveContainer [
                  responsiveContainer.width opts.ResponsiveContainer.Width
                  responsiveContainer.height opts.ResponsiveContainer.Height

                  responsiveContainer.chart (
                     match state.SelectedTimeSeriesChart with
                     | SelectedTimeSeriesChart.TimeSeries ->
                        renderTimeSeriesByOrgChart
                           timeSeries.ByOrg
                           state.SelectedTimeSeriesDateFilter
                           state.SelectedTimeSeriesYAxis
                     | SelectedTimeSeriesChart.TimeSeriesByAccount ->
                        renderTimeSeriesByAccountChart
                           timeSeries.ByAccount
                           org.Accounts
                           state.SelectedTimeSeriesDateFilter
                           state.SelectedTimeSeriesYAxis
                  )
               ]

            classyNode Html.div [ "analytics-top-money-flow" ] [
               classyNode Html.div [ "analytics-topN-filter"; "grid" ] [
                  Html.h4 "Top Money Flow"
                  renderTopNMonthSelect
                     state.SelectedTopNMonth
                     (Msg.SelectTopNMonth >> dispatch)
               ]

               Html.progress [
                  if not state.TopNUpdateInProgress then
                     attr.value 100
               ]

               match analytics.TopN, analytics.TimeSeriesMonthly with
               | _, None
               | None, _ -> Html.p "No money movement for this period."
               | Some topN, Some timeSeries ->
                  classyNode Html.div [ "grid" ] [
                     Html.article [
                        renderTopMoneyListItems
                           MoneyFlow.In
                           org.Accounts
                           topN
                           timeSeries
                           state.SelectedTopNMonth
                     ]

                     Html.article [
                        renderTopMoneyListItems
                           MoneyFlow.Out
                           org.Accounts
                           topN
                           timeSeries
                           state.SelectedTopNMonth
                     ]
                  ]
            ]

            Html.br []

            classyNode Html.div [ "analytics-top-purchasers" ] [
               classyNode Html.div [ "analytics-topN-filter"; "grid" ] [
                  Html.h4 "Top Purchasers"
                  renderTopNMonthSelect
                     state.SelectedTopNPurchasersMonth
                     (Msg.SelectTopNPurchasersMonth >> dispatch)
               ]

               Html.progress [
                  if not state.TopNPurchasersUpdateInProgress then
                     attr.value 100
               ]

               match analytics.TopNPurchasers with
               | None -> Html.p "No employee purchases for this period."
               | Some topN -> Html.article [ renderTopPurchasers topN ]
            ]
         | _, Deferred.Resolved(Ok None) ->
            Html.p "Can not display analytics. Organization not found."
         | _ -> Html.progress []
      ]
   ]

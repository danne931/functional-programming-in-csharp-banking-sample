module Bank.Analytics.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder

open Bank.Analytics.Api
open RoutePaths
open Lib.NetworkQuery
open Lib.Time
open Lib.SharedTypes
open Bank.UserSession.Middleware

let startAnalyticsRoutes (app: WebApplication) =
   app
      .MapGet(
         AnalyticsPath.Get,
         Func<Guid, string, Nullable<int>, string, Task<IResult>>
            (fun
                 ([<FromRoute>] orgId)
                 ([<FromQuery>] moneyFlowTopNDate)
                 ([<FromQuery>] moneyFlowTopNLimit)
                 ([<FromQuery>] moneyFlowTimeSeriesDateRange) ->
               let orgId = OrgId orgId

               match
                  Option.ofNullable moneyFlowTopNLimit,
                  DateTime.parseOptional moneyFlowTopNDate,
                  dateRangeFromQueryString moneyFlowTimeSeriesDateRange
               with
               | Some topNLimit, Some topNDate, Some(st, en) ->
                  let txnQuery = {
                     OrgId = orgId
                     MoneyFlowTopN = {
                        OrgId = orgId
                        Limit = topNLimit
                        Date = topNDate
                     }
                     MoneyFlowDailyTimeSeries = {
                        OrgId = orgId
                        Start = st
                        End = en
                     }
                     MoneyFlowMonthlyTimeSeries = {
                        FilterBy = MoneyFlowMonthlyTimeSeriesFilterBy.Org orgId
                        LookbackMonths = 3
                     }
                  }

                  moneyFlowAnalytics txnQuery |> RouteUtil.unwrapTaskResult
               | None, None, Some(st, en) ->
                  moneyFlowDailyTimeSeriesAnalytics {
                     OrgId = orgId
                     Start = st
                     End = en
                  }
                  |> RouteUtil.unwrapTaskResultOption
               | Some topNLimit, Some topNDate, None ->
                  moneyFlowTopNAnalytics {
                     OrgId = orgId
                     Limit = topNLimit
                     Date = topNDate
                  }
                  |> RouteUtil.unwrapTaskResultOption
               | _ -> 422 |> Results.StatusCode |> Task.FromResult)
      )
      .RBAC(Permissions.GetTransactions)
   |> ignore

   app
      .MapGet(
         AnalyticsPath.MoneyFlowMonthlyTimeSeriesForAccount,
         Func<Guid, Task<IResult>>(fun accountId ->
            moneyFlowMonthlyTimeSeriesAnalytics {
               FilterBy =
                  MoneyFlowMonthlyTimeSeriesFilterBy.Account(
                     AccountId accountId
                  )
               LookbackMonths = 3
            }
            |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetTransactions)
   |> ignore

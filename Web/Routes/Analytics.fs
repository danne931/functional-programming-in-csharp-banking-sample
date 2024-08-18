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
         Func<Guid, string, Nullable<int>, Nullable<int>, string, Task<IResult>>
            (fun
                 ([<FromRoute>] orgId)
                 ([<FromQuery>] topNDate)
                 ([<FromQuery>] purchasersTopNLimit)
                 ([<FromQuery>] moneyFlowTopNLimit)
                 ([<FromQuery>] moneyFlowTimeSeriesDateRange) ->
               let orgId = OrgId orgId

               match
                  Option.ofNullable moneyFlowTopNLimit,
                  Option.ofNullable purchasersTopNLimit,
                  DateTime.parseOptional topNDate,
                  dateRangeFromQueryString moneyFlowTimeSeriesDateRange
               with
               | Some topNMoneyFlowLimit,
                 Some topNPurchasersLimit,
                 Some topNDate,
                 Some(st, en) ->
                  let txnQuery = {
                     OrgId = orgId
                     TopNMoneyFlow = {
                        OrgId = orgId
                        Limit = topNMoneyFlowLimit
                        Date = topNDate
                     }
                     TopNPurchasers = {
                        OrgId = orgId
                        Limit = topNPurchasersLimit
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
               | None, None, None, Some(st, en) ->
                  moneyFlowDailyTimeSeriesAnalytics {
                     OrgId = orgId
                     Start = st
                     End = en
                  }
                  |> RouteUtil.unwrapTaskResultOption
               | Some topNMoneyFlowLimit, None, Some topNDate, None ->
                  moneyFlowTopNAnalytics {
                     OrgId = orgId
                     Limit = topNMoneyFlowLimit
                     Date = topNDate
                  }
                  |> RouteUtil.unwrapTaskResultOption
               | None, Some topNPurchasersLimit, Some topNDate, None ->
                  employeePurchaseTopNAnalytics {
                     OrgId = orgId
                     Limit = topNPurchasersLimit
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

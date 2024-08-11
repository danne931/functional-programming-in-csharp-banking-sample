module AnalyticsService

open Feliz.Router

open Fable.SimpleHttp
open Lib.SharedTypes
open RoutePaths
open Bank.Account.Domain
open UIDomain

let private serviceName = "Analytics Service"
let private topNLimit = "5"

let loadInitialAnalytics
   (orgId: OrgId)
   (dateFilter: DateFilter)
   : Async<Result<MoneyFlowAnalytics, Err>>
   =
   async {
      let path =
         AnalyticsPath.get orgId
         + Router.encodeQueryString (
            [
               "moneyFlowTopNDate", DateTime.toISOString System.DateTime.UtcNow
               "moneyFlowTopNLimit", topNLimit
               "moneyFlowTimeSeriesDateRange",
               DateFilter.toQueryString dateFilter
            ]
         )

      let! (code, responseText) = Http.get path

      if code <> 200 then
         return Error(Err.InvalidStatusCodeError(serviceName, code))
      else
         return Serialization.deserialize<MoneyFlowAnalytics> responseText
   }

let loadTimeSeriesAnalytics
   (orgId: OrgId)
   (dateFilter: DateFilter)
   : Async<Result<MoneyFlowDailyTimeSeriesAnalytics option, Err>>
   =
   async {
      let path =
         AnalyticsPath.get orgId
         + Router.encodeQueryString (
            [
               "moneyFlowTimeSeriesDateRange",
               DateFilter.toQueryString dateFilter
            ]
         )

      let! (code, responseText) = Http.get path

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error(Err.InvalidStatusCodeError(serviceName, code))
      else
         return
            Serialization.deserialize<MoneyFlowDailyTimeSeriesAnalytics>
               responseText
            |> Result.map Some
   }

let loadTopNAnalytics
   (orgId: OrgId)
   (date: System.DateTime)
   : Async<Result<MoneyFlowTopNAnalytics option, Err>>
   =
   async {
      let path =
         AnalyticsPath.get orgId
         + Router.encodeQueryString (
            [
               "moneyFlowTopNDate", DateTime.toISOString date
               "moneyFlowTopNLimit", topNLimit
            ]
         )

      let! (code, responseText) = Http.get path

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error(Err.InvalidStatusCodeError(serviceName, code))
      else
         return
            Serialization.deserialize<MoneyFlowTopNAnalytics> responseText
            |> Result.map Some
   }

module AnalyticsService

open Feliz.Router
open System

open Fable.SimpleHttp
open Lib.SharedTypes
open RoutePaths
open Bank.Account.Domain
open UIDomain

let private serviceName = "Analytics Service"
let private topNLimit = "5"
let private topNPurchasersLimit = "10"

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
               "topNDate", DateTime.toISOString DateTime.UtcNow
               "moneyFlowTopNLimit", topNLimit
               "moneyFlowTimeSeriesDateRange",
               DateFilter.toQueryString dateFilter

               "purchasersTopNLimit", topNPurchasersLimit
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
   (date: DateTime)
   : Async<Result<MoneyFlowTopNAnalytics option, Err>>
   =
   async {
      let path =
         AnalyticsPath.get orgId
         + Router.encodeQueryString (
            [
               "topNDate", DateTime.toISOString date
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

let loadTopNPurchasersAnalytics
   (orgId: OrgId)
   (date: DateTime)
   : Async<Result<EmployeePurchaserTopN list option, Err>>
   =
   async {
      let path =
         AnalyticsPath.get orgId
         + Router.encodeQueryString (
            [
               "topNDate", DateTime.toISOString date
               "purchasersTopNLimit", topNPurchasersLimit
            ]
         )

      let! (code, responseText) = Http.get path

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error(Err.InvalidStatusCodeError(serviceName, code))
      else
         return
            Serialization.deserialize<EmployeePurchaserTopN list> responseText
            |> Result.map Some
   }

let loadMoneyFlowMonthlyTimeSeriesForAccount
   (accountId: AccountId)
   : Async<Result<MoneyFlowMonthlyTimeSeriesAnalytics option, Err>>
   =
   async {
      let path = AnalyticsPath.moneyFlowMonthlyTimeSeriesForAccount accountId

      let! (code, responseText) = Http.get path

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error(Err.InvalidStatusCodeError(serviceName, code))
      else
         return
            Serialization.deserialize<MoneyFlowMonthlyTimeSeriesAnalytics>
               responseText
            |> Result.map Some
   }

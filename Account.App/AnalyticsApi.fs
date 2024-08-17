module Bank.Analytics.Api

open System
open System.Threading.Tasks
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Lib.Postgres
open Lib.Time
open Bank.Account.Domain
open TransactionSqlMapper

module Fields = TransactionFields
module Writer = TransactionSqlWriter
module Reader = TransactionSqlReader

type private MoneyFlowAnalyticsDBResult =
   | TimeSeriesDaily of MoneyFlowDailyTimeSeriesAnalytics option
   | TimeSeriesMonthly of MoneyFlowMonthlyTimeSeriesAnalytics option
   | TopN of MoneyFlowTopNAnalytics option

type MoneyFlowTopNQuery = {
   OrgId: OrgId
   Limit: int
   Date: DateTime
}

type MoneyFlowDailyTimeSeriesQuery = {
   OrgId: OrgId
   Start: DateTime
   End: DateTime
}

[<RequireQualifiedAccess>]
type MoneyFlowMonthlyTimeSeriesFilterBy =
   | Org of OrgId
   | Account of AccountId

type MoneyFlowMonthlyTimeSeriesQuery = {
   FilterBy: MoneyFlowMonthlyTimeSeriesFilterBy
   LookbackMonths: int
}

type MoneyFlowAnalyticsQuery = {
   OrgId: OrgId
   MoneyFlowTopN: MoneyFlowTopNQuery
   MoneyFlowDailyTimeSeries: MoneyFlowDailyTimeSeriesQuery
   MoneyFlowMonthlyTimeSeries: MoneyFlowMonthlyTimeSeriesQuery
}

let moneyFlowTopNAnalytics
   (txnQuery: MoneyFlowTopNQuery)
   : Task<Result<MoneyFlowTopNAnalytics option, Err>>
   =
   taskResultOption {
      let query =
         $"""
         SELECT * FROM {TransactionFunctions.moneyFlowTopNMonthly}(
            @orgId,
            @flowIn::{TransactionTypeCast.moneyFlow},
            @topN,
            @date
         )

         UNION ALL

         SELECT * FROM {TransactionFunctions.moneyFlowTopNMonthly}(
            @orgId,
            @flowOut::{TransactionTypeCast.moneyFlow},
            @topN,
            @date
         )
         """

      let qParams = [
         "orgId", txnQuery.OrgId |> OrgId.get |> Sql.uuid
         "flowIn", MoneyFlow.In |> string |> Sql.string
         "flowOut", MoneyFlow.Out |> string |> Sql.string
         "topN", Sql.int txnQuery.Limit
         "date", Sql.timestamptz txnQuery.Date
      ]

      let! topN =
         pgQuery<MoneyFlowTopN> query (Some qParams) (fun read -> {
            MoneyFlow =
               match Reader.moneyFlow read with
               | Some flow -> flow
               | None ->
                  failwith "Error attempting to cast string to MoneyFlow"
            Amount = read.decimal Fields.amount
            Source = read.string Fields.source
         })

      return
         topN
         |> List.partition (fun a -> a.MoneyFlow = MoneyFlow.In)
         |> fun (mfIn, mfOut) -> { In = mfIn; Out = mfOut }
   }

let moneyFlowDailyTimeSeriesAnalytics
   (txnQuery: MoneyFlowDailyTimeSeriesQuery)
   : Task<Result<MoneyFlowDailyTimeSeriesAnalytics option, Err>>
   =
   taskResultOption {
      let endDateAdjustedToPreviousDay =
         if DateTime.isToday txnQuery.End then
            Some <| txnQuery.End.AddDays(-1)
         else
            None

      let qParams = [
         "orgId", txnQuery.OrgId |> OrgId.get |> Sql.uuid
         "startDate", Sql.timestamptz txnQuery.Start
         "endDate",
         endDateAdjustedToPreviousDay
         |> Option.defaultValue txnQuery.End
         |> Sql.timestamptz
      ]

      let query =
         $"""
         SELECT
            bh.account_id,
            bh.date,
            tsd.amount_in,
            tsd.amount_out,

            COALESCE(
               LAG(bh.balance) OVER (PARTITION BY bh.account_id ORDER BY bh.date),
               0
            ) AS prev_balance,

            bh.balance
         FROM {TransactionFunctions.moneyFlowTimeSeriesDaily}(
            @orgId,
            @startDate,
            @endDate
         ) tsd
         JOIN balance_history bh
            ON tsd.day = bh.date AND tsd.account_id = bh.account_id
         """

      // NOTE: A balance history record is created daily for the previous day.
      // If the txnQuery.End falls on the current day then we need to compute
      // today's balance from the previous day balance history record with
      // the sum of todays transaction amounts.
      let query =
         if endDateAdjustedToPreviousDay.IsSome then
            query
            + $"""
            UNION ALL

            SELECT
               t.account_id,
               t.date,
               t.amount_in,
               t.amount_out,
               COALESCE(bh.balance, 0) AS prev_balance,
               COALESCE(bh.balance, 0) + t.daily_diff AS balance
            FROM (
               SELECT
                  t.account_id,
                  t.day AS date,
                  t.amount_in,
                  t.amount_out,
                  t.amount_in - t.amount_out AS daily_diff
               FROM {TransactionFunctions.moneyFlowTimeSeriesDaily}(
                  @orgId,
                  CURRENT_DATE,
                  CURRENT_DATE
               ) t
            ) t
            JOIN balance_history bh
               ON bh.account_id = t.account_id
               AND bh.date = t.date - interval '1 day'
            """
         else
            query

      let! series =
         pgQuery<MoneyFlowDailyTimeSeriesByAccount>
            query
            (Some qParams)
            (fun read ->
               let prevBalance = read.decimal "prev_balance"
               let balance = read.decimal "balance"

               {
                  Day = read.dateTime "date"
                  AccountId = Reader.accountId read
                  AmountIn = read.decimal "amount_in"
                  AmountOut = read.decimal "amount_out"
                  BalanceHistory = {
                     Balance = balance
                     PreviousBalance = prevBalance
                     PercentChange =
                        if prevBalance = 0m then
                           0m
                        else
                           (balance - prevBalance) / prevBalance * 100m
                  }
               })

      return {
         ByAccount = series
         ByOrg =
            series
            |> List.groupBy _.Day
            |> List.map (fun (day, series) ->
               series
               |> List.fold
                  (fun acc item -> {
                     Day = day
                     BalanceHistory = {
                        Balance =
                           acc.BalanceHistory.Balance
                           + item.BalanceHistory.Balance
                        PreviousBalance =
                           acc.BalanceHistory.PreviousBalance
                           + item.BalanceHistory.PreviousBalance
                        PercentChange =
                           acc.BalanceHistory.PercentChange
                           + item.BalanceHistory.PercentChange
                     }
                     AmountIn = acc.AmountIn + item.AmountIn
                     AmountOut = acc.AmountOut + item.AmountOut
                  })
                  {
                     Day = day
                     BalanceHistory = {
                        Balance = 0m
                        PreviousBalance = 0m
                        PercentChange = 0m
                     }
                     AmountIn = 0m
                     AmountOut = 0m
                  })
      }
   }

let moneyFlowMonthlyTimeSeriesAnalytics
   (txnQuery: MoneyFlowMonthlyTimeSeriesQuery)
   : Task<Result<MoneyFlowMonthlyTimeSeriesAnalytics option, Err>>
   =
   taskResultOption {
      let qParams = [
         "filterBy",
         match txnQuery.FilterBy with
         | MoneyFlowMonthlyTimeSeriesFilterBy.Account _ -> Sql.string "account"
         | MoneyFlowMonthlyTimeSeriesFilterBy.Org _ -> Sql.string "org"

         "filterId",
         match txnQuery.FilterBy with
         | MoneyFlowMonthlyTimeSeriesFilterBy.Account accountId ->
            accountId |> AccountId.get |> Sql.uuid
         | MoneyFlowMonthlyTimeSeriesFilterBy.Org orgId ->
            orgId |> OrgId.get |> Sql.uuid

         "lookbackMonths", Sql.int txnQuery.LookbackMonths
      ]

      let query =
         $"SELECT * 
           FROM {TransactionFunctions.moneyFlowTimeSeriesMonthly}(
              @filterBy::{TransactionTypeCast.timeSeriesMonthlyFilterBy},
              @filterId,
              @lookbackMonths
           )"

      let! series =
         pgQuery<MoneyFlowMonthlyTimeSeries> query (Some qParams) (fun read -> {
            Month = read.dateTime "month"
            AmountIn = read.decimal "amount_in"
            AmountOut = read.decimal "amount_out"
         })

      let avg sum =
         Math.Round(sum / decimal series.Length, 2)

      return {
         TimeSeries = series
         AverageIn = series |> List.sumBy _.AmountIn |> avg
         AverageOut = series |> List.sumBy _.AmountOut |> avg
      }
   }

let moneyFlowAnalytics
   (txnQuery: MoneyFlowAnalyticsQuery)
   : Task<Result<MoneyFlowAnalytics, Err>>
   =
   taskResult {
      let mfTopNTask =
         moneyFlowTopNAnalytics txnQuery.MoneyFlowTopN
         |> TaskResult.map MoneyFlowAnalyticsDBResult.TopN

      let mfDailyTimeSeriesTask =
         moneyFlowDailyTimeSeriesAnalytics txnQuery.MoneyFlowDailyTimeSeries
         |> TaskResult.map MoneyFlowAnalyticsDBResult.TimeSeriesDaily

      let mfMonthlyTimeSeriesTask =
         moneyFlowMonthlyTimeSeriesAnalytics txnQuery.MoneyFlowMonthlyTimeSeries
         |> TaskResult.map MoneyFlowAnalyticsDBResult.TimeSeriesMonthly

      let! res =
         Task.WhenAll [|
            mfTopNTask
            mfDailyTimeSeriesTask
            mfMonthlyTimeSeriesTask
         |]

      let! res = res |> List.ofArray |> List.traverseResultM id

      let res =
         match res with
         | [ MoneyFlowAnalyticsDBResult.TopN topNOpt
             MoneyFlowAnalyticsDBResult.TimeSeriesDaily seriesDailyOpt
             MoneyFlowAnalyticsDBResult.TimeSeriesMonthly seriesMonthlyOpt ] -> {
            TimeSeriesMonthly = seriesMonthlyOpt
            TimeSeriesDaily = seriesDailyOpt
            TopN = topNOpt
           }
         | _ -> {
            TimeSeriesMonthly = None
            TimeSeriesDaily = None
            TopN = None
           }

      return res
   }

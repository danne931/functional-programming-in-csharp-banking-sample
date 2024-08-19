namespace Bank.Account.Domain

open System

open Lib.SharedTypes

type EmployeePurchaserTopN = {
   Amount: decimal
   EmployeeId: EmployeeId
   EmployeeName: string
}

type MoneyFlowTopN = {
   MoneyFlow: MoneyFlow
   Amount: decimal
   Source: string
}

type BalanceHistory = {
   Balance: decimal
   PreviousBalance: decimal
   PercentChange: decimal
}

type MoneyFlowDailyTimeSeriesByOrg = {
   Day: DateTime
   AmountIn: decimal
   AmountOut: decimal
   BalanceHistory: BalanceHistory
}

type MoneyFlowDailyTimeSeriesByAccount = {
   Day: DateTime
   AccountId: AccountId
   AmountIn: decimal
   AmountOut: decimal
   BalanceHistory: BalanceHistory
}

type MoneyFlowTopNAnalytics = {
   In: MoneyFlowTopN list
   Out: MoneyFlowTopN list
}

type MoneyFlowDailyTimeSeriesAnalytics = {
   ByOrg: MoneyFlowDailyTimeSeriesByOrg list
   ByAccount: MoneyFlowDailyTimeSeriesByAccount list
}

type MoneyFlowMonthlyTimeSeries = {
   Month: DateTime
   AmountIn: decimal
   AmountOut: decimal
}

type MoneyFlowMonthlyTimeSeriesAnalytics = {
   TimeSeries: MoneyFlowMonthlyTimeSeries list
   AverageIn: decimal
   AverageOut: decimal
}

type MoneyFlowAnalytics = {
   TimeSeriesMonthly: MoneyFlowMonthlyTimeSeriesAnalytics option
   TimeSeriesDaily: MoneyFlowDailyTimeSeriesAnalytics option
   TopN: MoneyFlowTopNAnalytics option
   TopNPurchasers: EmployeePurchaserTopN list option
}

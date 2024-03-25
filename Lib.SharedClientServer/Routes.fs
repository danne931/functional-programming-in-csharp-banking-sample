module RoutePaths

open System

module AccountPath =
   let Base = "/accounts"
   let account (id: Guid) = Base + $"/{id}"
   let Account = Base + "/{id}"
   let Deposit = Base + "/deposit"
   let Debit = Base + "/debit"
   let DailyDebitLimit = Base + "/daily-debit-limit"
   let LockCard = Base + "/lock"
   let UnlockCard = Base + "/unlock"
   let CloseAccount = Base + "/close-account"

   let billingStatement (accountId: Guid) (offset: int) =
      $"{Base}/billing-statement/{accountId}/{offset}"

   let BillingStatement = Base + "/billing-statement/{accountId}/{offset}"

module DiagnosticPath =
   let Base = "/diagnostic"
   let Account = Base + "/account/{id}"
   let AccountEvents = Base + "/events/{id}"
   let LoadTest = Base + "/load-test"
   let LoadTestProgress = LoadTest + "/progress"
   let LoadTestTeardown = LoadTest + "/teardown"
   let CircuitBreaker = Base + "/circuit-breaker"

module TransferPath =
   let Base = "/transfers"
   let TransferRecipient = Base + "/register-recipient"

module UserPath =
   let Base = "/users"

module RoutePaths

open System

module AccountPath =
   let Base = "/accounts"
   let account (id: Guid) = $"{Base}/{id}"
   let Account = Base + "/{id}"
   let accountAndTransactions (id: Guid) = $"{account id}/transactions"
   let AccountAndTransactions = Account + "/transactions"
   let Deposit = Base + "/deposit"
   let Debit = Base + "/debit"
   let DailyDebitLimit = Base + "/daily-debit-limit"
   let LockCard = Base + "/lock"
   let UnlockCard = Base + "/unlock"
   let CloseAccount = Base + "/close-account"
   let BillingStatement = Base + "/billing-statement/{accountId}/{page}"

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

module TransactionPath =
   let Base = "/transactions"
   let AccountTransactions = Base + "/{id}"
   let accountTransactions (id: Guid) = $"{Base}/{id}"
   let Categories = "/transaction-categories"
   let TransactionInfo = Base + "/transaction/{txnId}"
   let transactionInfo (txnId: Guid) = $"{Base}/transaction/{txnId}"
   let Category = TransactionInfo + "/category/{categoryId}"
   let CategoryDelete = TransactionInfo + "/category"

   let category (txnId: Guid) (categoryId: int) =
      transactionInfo txnId + $"/category/{categoryId}"

   let categoryDelete (txnId: Guid) = transactionInfo txnId + "/category"
   let Note = TransactionInfo + "/note"
   let note (txnId: Guid) = transactionInfo txnId + "/note"

module UserPath =
   let Base = "/users"

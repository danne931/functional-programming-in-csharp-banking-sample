module RoutePaths

open Lib.SharedTypes

module AccountPath =
   let Base = "/accounts"
   let account (id: AccountId) = $"{Base}/{id}"
   let Account = Base + "/{id}"
   let accountAndTransactions (id: AccountId) = $"{account id}/transactions"
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
   let Internal = Base + "/internal"
   let Domestic = Base + "/domestic"
   let InternalTransferRecipient = Internal + "/register-recipient"
   let DomesticTransferRecipient = Domestic + "/register-recipient"
   let DomesticTransferRecipientEdit = Domestic + "/edit-recipient"
   let NicknameRecipient = Base + "/recipient-nickname"

module TransactionPath =
   let Base = "/transactions"
   let AccountTransactions = Base + "/{id}"
   let accountTransactions (id: AccountId) = $"{Base}/{id}"
   let Categories = Base + "/categories"
   let Merchants = Base + "/merchants/{orgId}"
   let merchants (orgId: OrgId) = $"{Base}/merchants/{orgId}"

   let TransactionInfo = Base + "/transaction/{txnId}"

   let transactionInfo (txnId: EventId) =
      let (EventId id) = txnId
      $"{Base}/transaction/{id}"

   let Category = TransactionInfo + "/category/{categoryId}"
   let CategoryDelete = TransactionInfo + "/category"

   let category (txnId: EventId) (categoryId: int) =
      transactionInfo txnId + $"/category/{categoryId}"

   let categoryDelete (txnId: EventId) = transactionInfo txnId + "/category"
   let Note = TransactionInfo + "/note"
   let note (txnId: EventId) = transactionInfo txnId + "/note"

module UserPath =
   let Base = "/users"

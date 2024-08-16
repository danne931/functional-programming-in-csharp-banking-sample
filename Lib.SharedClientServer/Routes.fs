module RoutePaths

open Lib.SharedTypes

let private API = "/api"

module OrgPath =
   let Base = $"{API}/org"
   let Get = Base + "/{orgId}"
   let get (orgId: OrgId) = $"{Base}/{orgId}"

   let Search = Base + "/search/{orgId}/{searchQuery}"

   let search (orgId: OrgId) (searchQuery: string) =
      $"{Base}/search/{orgId}/{searchQuery}"

module AccountPath =
   let Base = $"{API}/accounts"
   let account (id: AccountId) = $"{Base}/{id}"
   let Account = Base + "/{id}"
   let accountAndTransactions (id: AccountId) = $"{account id}/transactions"
   let AccountAndTransactions = Account + "/transactions"
   let Deposit = Base + "/deposit"
   let CloseAccount = Base + "/close-account"
   let BillingStatement = Base + "/billing-statement/{accountId}/{page}"

module DiagnosticPath =
   let Base = $"{API}/diagnostic"
   let Account = Base + "/account/{id}"
   let AccountEvents = Base + "/events/{id}"
   let LoadTest = Base + "/load-test"
   let LoadTestProgress = LoadTest + "/progress"
   let LoadTestTeardown = LoadTest + "/teardown"
   let CircuitBreaker = Base + "/circuit-breaker"

module TransferPath =
   let Base = $"{API}/transfers"
   let InternalWithinOrg = Base + "/internal-within-org"
   let InternalCrossOrg = Base + "/internal-cross-org"
   let Domestic = Base + "/domestic"
   let DomesticTransferRecipient = Domestic + "/register-recipient"
   let DomesticTransferRecipientEdit = Domestic + "/edit-recipient"
   let NicknameRecipient = Base + "/recipient-nickname"

module TransactionPath =
   let Base = $"{API}/transactions"
   let AccountTransactions = Base + "/{id}"
   let accountTransactions (id: AccountId) = $"{Base}/{id}"
   let Categories = Base + "/categories"
   let Merchants = Base + "/merchants/{orgId}"
   let merchants (orgId: OrgId) = $"{Base}/merchants/{orgId}"

   let TransactionInfo = Base + "/transaction/{txnId}"

   let transactionInfo (id: EventId) = $"{Base}/transaction/{id}"

   let TransactionConfirmation =
      Base + "/transaction-confirmation/{correlationId}"

   let transactionConfirmation (correlationId: CorrelationId) =
      $"{Base}/transaction-confirmation/{correlationId}"

   let Category = TransactionInfo + "/category/{categoryId}"
   let CategoryDelete = TransactionInfo + "/category"

   let category (txnId: EventId) (categoryId: int) =
      transactionInfo txnId + $"/category/{categoryId}"

   let categoryDelete (txnId: EventId) = transactionInfo txnId + "/category"
   let Note = TransactionInfo + "/note"
   let note (txnId: EventId) = transactionInfo txnId + "/note"

module EmployeePath =
   let Base = $"{API}/employees"
   let Get = Base + "/{orgId}"
   let get (orgId: OrgId) = $"{Base}/{orgId}"
   let Search = Base + "/search/{orgId}/{searchQuery}"

   let search (orgId: OrgId) (searchQuery: string) =
      $"{Base}/search/{orgId}/{searchQuery}"

   let UpdateRole = Base + "/role"
   let CancelEmployeeInvitation = Base + "/cancel-employee-invitation"
   let ResendInviteNotification = Base + "/resend-invite-notification"
   let RestoreAccess = Base + "/restore-access"
   let History = Get + "/history"
   let history (orgId: OrgId) = get orgId + "/history"

module CardPath =
   let Base = $"{API}/cards"
   let Get = Base + "/{orgId}"
   let get (orgId: OrgId) = $"{Base}/{orgId}"
   let DailyPurchaseLimit = Base + "/daily-purchase-limit"
   let MonthlyPurchaseLimit = Base + "/monthly-purchase-limit"
   let LockCard = Base + "/lock"
   let UnlockCard = Base + "/unlock"
   let Purchase = Base + "/purchase"
   let UpdateNickname = Base + "/nickname"

module UserSessionPath =
   let Login = "/login"
   let GetSession = "/session"
   let AuthorizeInvite = "/auth/invite"
   let AuthorizationCallback = "/auth/callback"

module AnalyticsPath =
   let Base = $"{API}/analytics"
   let Get = Base + "/{orgId}"
   let get (orgId: OrgId) = $"{Base}/{orgId}"

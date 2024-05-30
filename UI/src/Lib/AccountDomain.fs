module Bank.Account.UIDomain

open System

open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes
open Lib.TransactionQuery

type AccountProfilesMaybe = Result<Map<AccountId, AccountProfile> option, Err>

type AccountAndTransactionsMaybe =
   Result<(Account * AccountEvent list) option, Err>

type TransactionsMaybe = Result<AccountEvent list option, Err>

type TransactionUIFriendly = {
   DateNaked: DateTime
   Date: string
   Name: string
   Origin: string option
   AmountNaked: decimal option
   Amount: string option
   Sign: string
   Info: string option
   MoneyFlow: MoneyFlow
   Source: string
   Destination: string
}

let dateUIFriendly (date: DateTime) =
   let dayAndMonth = date.ToLongDateString().Split(string date.Year)[0]
   $"{dayAndMonth} {date.ToShortTimeString()}"

let transactionUIFriendly
   (account: Account)
   (txn: AccountEvent)
   : TransactionUIFriendly
   =
   let _, envelope = AccountEnvelope.unwrap txn

   let props = {
      DateNaked = envelope.Timestamp
      Date = dateUIFriendly envelope.Timestamp
      Name = envelope.EventName
      Origin = None
      AmountNaked = None
      Amount = None
      Sign = ""
      Info = None
      MoneyFlow = MoneyFlow.None
      Source = ""
      Destination = ""
   }

   let accountName =
      account.Name + " **" + (string account.EntityId).Substring(-4)

   let recipientName (recipient: TransferRecipient) : string =
      account.TransferRecipients
      |> Map.tryFind recipient.LookupKey
      |> Option.bind _.Nickname
      |> Option.defaultValue recipient.Name

   let props =
      match txn with
      | CreatedAccount evt -> {
         props with
            Name = "Account Created"
            AmountNaked = Some evt.Data.Balance
            MoneyFlow = MoneyFlow.In
        }
      | DepositedCash evt -> {
         props with
            Name = "Deposit"
            AmountNaked = Some evt.Data.DepositedAmount
            Origin = Some evt.Data.Origin
            MoneyFlow = MoneyFlow.In
            Source = evt.Data.Origin
            Destination = accountName
        }
      | DebitedAccount evt -> {
         props with
            Name = "Debit"
            AmountNaked = Some evt.Data.DebitedAmount
            Origin = Some evt.Data.Origin
            MoneyFlow = MoneyFlow.Out
            Source = accountName
            Destination = evt.Data.Origin
        }
      | MaintenanceFeeDebited evt -> {
         props with
            Name = "Maintenance Fee"
            AmountNaked = Some evt.Data.DebitedAmount
            MoneyFlow = MoneyFlow.Out
        }
      | MaintenanceFeeSkipped _ -> {
         props with
            Name = "Maintenance Fee Skipped"
        }
      | DailyDebitLimitUpdated evt -> {
         props with
            Name = "Daily Debit Limit Updated"
            AmountNaked = Some evt.Data.DebitLimit
        }
      | InternalTransferRecipient evt -> {
         props with
            Name = "Transfer Recipient Added"
            Info = Some $"Recipient: {evt.Data.FirstName} {evt.Data.LastName}"
        }
      | DomesticTransferRecipient evt -> {
         props with
            Name = "Domestic Transfer Recipient Added"
            Info = Some $"Recipient: {evt.Data.FirstName} {evt.Data.LastName}"
        }
      | InternalSenderRegistered evt -> {
         props with
            Name = "Transfer Sender Registered"
            Info =
               Some
                  $"{evt.Data.TransferSender.Name} added this account as a transfer recipient."
        }
      | InternalRecipientDeactivated evt -> {
         props with
            Name = "Recipient Deactivated"
            Info =
               Some $"Recipient {evt.Data.RecipientName} closed their account"
        }
      | TransferPending evt -> {
         props with
            Name = "Transfer Request"
            Info = Some $"Recipient: {recipientName evt.Data.Recipient}"
            AmountNaked = Some evt.Data.DebitedAmount
            MoneyFlow = MoneyFlow.Out
            Source = accountName
            Destination =
               recipientName evt.Data.Recipient
               + " **"
               + evt.Data.Recipient.Identification.Substring(-4)
        }
      | TransferProgress evt -> {
         props with
            Name = "Transfer Progress Update"
            Info =
               Some
                  $"Status {evt.Data.Status} Recipient: {recipientName evt.Data.Recipient}"
            AmountNaked = Some evt.Data.DebitedAmount
        }
      | TransferApproved evt -> {
         props with
            Name = "Transfer Approved"
            Info = Some $"Recipient: {recipientName evt.Data.Recipient}"
            AmountNaked = Some evt.Data.DebitedAmount
        }
      | TransferRejected evt -> {
         props with
            Name = "Transfer Rejected"
            Info =
               Some
                  $"Recipient: {recipientName evt.Data.Recipient} - Reason {evt.Data.Reason} - Acount refunded"
            AmountNaked = Some evt.Data.DebitedAmount
            MoneyFlow = MoneyFlow.In
        }
      | TransferDeposited evt -> {
         props with
            Name = "Transfer Received"
            AmountNaked = Some evt.Data.DepositedAmount
            Origin = Some evt.Data.Origin
            MoneyFlow = MoneyFlow.In
            Source = evt.Data.Origin
            Destination = string evt.EntityId
        }
      | LockedCard _ -> { props with Name = "Card Locked" }
      | UnlockedCard _ -> { props with Name = "Card Unlocked" }
      | BillingCycleStarted _ -> {
         props with
            Name = "New Billing Cycle"
            Info =
               Some "Previous transactions consolidated into billing statement"
        }
      | AccountClosed evt -> {
         props with
            Name = "Account Closed"
            Info = evt.Data.Reference
        }
      | RecipientNicknamed evt -> {
         props with
            Name = "Recipent Nickname Updated"
            Info =
               match evt.Data.Nickname with
               | None -> Some "Nickname removed"
               | Some name -> Some $"Nickname updated to {name}"
        }

   let sign =
      match props.MoneyFlow with
      | MoneyFlow.In -> "+"
      | MoneyFlow.Out -> "-"
      | MoneyFlow.None -> props.Sign

   {
      props with
         Sign = sign
         Amount =
            props.AmountNaked
            |> Option.map (fun amount -> sign + Money.format amount)
   }

type PotentialInternalTransferRecipients =
   private | PotentialInternalTransferRecipients of
      Map<AccountId, AccountProfile>

module PotentialInternalTransferRecipients =
   let create (account: Account) (accounts: Map<AccountId, AccountProfile>) =
      let potentialRecipients =
         accounts
         |> Map.filter (fun id _ ->
            let accountInRecipients =
               account.TransferRecipients |> Map.containsKey (string id)

            id <> account.EntityId && not accountInRecipients)

      PotentialInternalTransferRecipients potentialRecipients

   let value (PotentialInternalTransferRecipients recipients) = recipients

[<RequireQualifiedAccess>]
type DateFilter =
   | Custom of dateStart: DateTime * dateEnd: DateTime
   | Last30Days
   | CurrentMonth
   | LastMonth
   | CurrentYear
   | LastYear

module DateFilter =
   let toDateRange (filter: DateFilter) =
      let endOfToday = DateTime.Today.AddDays(1).AddMilliseconds(-1)

      match filter with
      | DateFilter.Custom(startDate, endDate) -> startDate, endDate
      | DateFilter.Last30Days -> DateTime.Today.AddDays(-30), endOfToday
      | DateFilter.CurrentMonth ->
         DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), endOfToday
      | DateFilter.LastMonth ->
         let start = DateTime.Today.AddMonths -1

         let endDate =
            DateTime(
               start.Year,
               start.Month,
               DateTime.DaysInMonth(start.Year, start.Month)
            )

         DateTime(start.Year, start.Month, 1),
         endDate.AddDays(1).AddMilliseconds(-1)
      | DateFilter.CurrentYear ->
         DateTime(DateTime.Today.Year, 1, 1), endOfToday
      | DateFilter.LastYear ->
         DateTime(DateTime.Today.AddYears(-1).Year, 1, 1),
         DateTime(DateTime.Today.Year, 1, 1).AddMilliseconds(-1)

[<RequireQualifiedAccess>]
type AccountActionView =
   | Debit
   | Deposit
   | Transfer
   | RegisterTransferRecipient
   | DailyDebitLimit
   | CardAccess

type AccountBrowserQuery = {
   MoneyFlow: MoneyFlow option
   Category: CategoryFilter option
   Amount: AmountFilter option
   Date: DateFilter option
   Action: AccountActionView option
   Transaction: EventId option
}

module AccountBrowserQuery =
   let toQueryParams (query: AccountBrowserQuery) : (string * string) list =
      let agg =
         match query.Amount with
         | None -> []
         | Some(AmountFilter.GreaterThanOrEqualTo amount) -> [
            "amountMin", string amount
           ]
         | Some(AmountFilter.LessThanOrEqualTo amount) -> [
            "amountMax", string amount
           ]
         | Some(AmountFilter.Between(min, max)) -> [
            "amountMin", string min
            "amountMax", string max
           ]

      let agg =
         match query.MoneyFlow with
         | None -> agg
         | Some flow -> ("moneyFlow", string flow) :: agg

      // If custom date range selected, date query param will consist
      // of a start & end date.  Otherwise it will be something like
      // date=Last30Days; date=LastMonth; etc.
      let agg =
         match query.Date with
         | None -> agg
         | Some(DateFilter.Custom(startDate, endDate)) ->
            ("date", DateTime.rangeAsQueryString startDate endDate) :: agg
         | Some filter -> ("date", string filter) :: agg

      let agg =
         match query.Category with
         | Some(CategoryFilter.CategoryIds catIds) ->
            ("categoryIds", TransactionQuery.categoryListToQueryString catIds)
            :: agg
         | Some(CategoryFilter.IsCategorized isCat) ->
            ("isCategorized", string isCat) :: agg
         | None -> agg

      let agg =
         match query.Action with
         | Some view -> ("action", string view) :: agg
         | None -> agg

      match query.Transaction with
      | Some txnId -> ("transaction", string txnId) :: agg
      | None -> agg

   let fromQueryParams
      (queryParams: (string * string) list)
      : AccountBrowserQuery
      =
      let queryParams = Map.ofList queryParams

      let findAmount key =
         Map.tryFind key queryParams |> Option.map decimal

      let min = findAmount "amountMin"
      let max = findAmount "amountMax"

      let amountOpt =
         match min, max with
         | Some min, None -> Some(AmountFilter.GreaterThanOrEqualTo min)
         | None, Some max -> Some(AmountFilter.LessThanOrEqualTo max)
         | Some min, Some max -> Some(AmountFilter.Between(min, max))
         | _ -> None

      {
         Category =
            Map.tryFind "categoryIds" queryParams
            |> Option.map TransactionQuery.categoryFromQueryString
            |> Option.orElse (
               Map.tryFind "isCategorized" queryParams
               |> Option.map (bool.Parse >> CategoryFilter.IsCategorized)
            )
         Amount = amountOpt
         MoneyFlow =
            Map.tryFind "moneyFlow" queryParams
            |> Option.bind MoneyFlow.fromString
         Date =
            Map.tryFind "date" queryParams
            |> Option.bind (function
               | "Last30Days" -> Some DateFilter.Last30Days
               | "CurrentMonth" -> Some DateFilter.CurrentMonth
               | "LastMonth" -> Some DateFilter.LastMonth
               | "CurrentYear" -> Some DateFilter.CurrentYear
               | "LastYear" -> Some DateFilter.LastYear
               | str ->
                  TransactionQuery.dateRangeFromQueryString str
                  |> Option.map DateFilter.Custom)
         Action =
            Map.tryFind "action" queryParams
            |> Option.bind (function
               | "Deposit" -> Some AccountActionView.Deposit
               | "Debit" -> Some AccountActionView.Debit
               | "Transfer" -> Some AccountActionView.Transfer
               | "RegisterRecipient" ->
                  Some AccountActionView.RegisterTransferRecipient
               | "DailyDebitLimit" -> Some AccountActionView.DailyDebitLimit
               | "CardAccess" -> Some AccountActionView.CardAccess
               | view ->
                  Log.error $"Account action view not implemented: {view}"
                  None)
         Transaction =
            Map.tryFind "transaction" queryParams
            |> Option.bind (Guid.parseOptional >> Option.map EventId)
      }

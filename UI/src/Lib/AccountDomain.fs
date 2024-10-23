module UIDomain.Account

open System

open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes
open Lib.NetworkQuery
open Lib.Time

type AccountProfilesMaybe = Result<Map<AccountId, AccountProfile> option, Err>

type AccountMaybe = Result<Account option, Err>

type TransactionsMaybe = Result<AccountEvent list option, Err>

type PaymentsMaybe = Result<PaymentSummary option, Err>

type AccountCommandReceipt = {
   PendingCommand: AccountCommand
   PendingEvent: AccountEvent
   PendingState: Account
   Envelope: Envelope
}

type TransactionUIFriendly = {
   Name: string
   DateNaked: DateTime
   Date: string
   Amount: string option
   Info: string
   MoneyFlow: MoneyFlow option
   Source: string option
   Destination: string option
}

let debitWithMerchantAlias
   (evt: BankEvent<DebitedAccount>)
   (merchants: Map<string, Merchant>)
   =
   {
      evt with
         Data.Origin =
            merchants
            |> Map.tryFind (evt.Data.Origin.ToLower())
            |> Option.bind _.Alias
            |> Option.defaultValue evt.Data.Origin
   }

let eventWithMerchantAlias
   (evt: AccountEvent)
   (merchants: Map<string, Merchant>)
   =
   match evt with
   | AccountEvent.DebitedAccount e ->
      AccountEvent.DebitedAccount(debitWithMerchantAlias e merchants)
   | _ -> evt

let autoTransferRuleDisplay (rule: AutomaticTransfer.AutomaticTransferRule) =
   match rule with
   | AutomaticTransfer.AutomaticTransferRule.ZeroBalance _ ->
      "Maintain a zero balance."
   | AutomaticTransfer.AutomaticTransferRule.TargetBalance rule ->
      let msg = $"Restore balance to {rule.TargetAccountBalance}."

      match rule.TargetBalanceRange with
      | None -> msg
      | Some range ->
         let low = range.LowerBound |> PositiveAmount.get |> Money.formatShort

         let upper = range.UpperBound |> PositiveAmount.get |> Money.formatShort

         $"{msg} Balance out of range ({low}, {upper})"
   | AutomaticTransfer.AutomaticTransferRule.PercentDistribution rule ->
      let rule = AutomaticTransfer.PercentDistributionRule.get rule
      $"Maintain a zero balance by distributing it among {rule.DestinationAccounts.Length} accounts."

let transactionUIFriendly
   (account: Account)
   (txn: AccountEvent)
   : TransactionUIFriendly
   =
   let _, envelope = AccountEnvelope.unwrap txn

   let props = {
      Name = envelope.EventName
      DateNaked = envelope.Timestamp
      Date = dateUIFriendly envelope.Timestamp
      Amount = None
      Info = ""
      MoneyFlow = None
      Source = None
      Destination = None
   }

   let domesticRecipientName (recipientFromEvt: DomesticTransferRecipient) =
      account.DomesticTransferRecipients
      |> Map.tryFind recipientFromEvt.AccountId
      |> Option.map _.FullName
      |> Option.defaultValue recipientFromEvt.FullName

   match txn with
   | CreatedAccount _ -> { props with Info = "Created Account" }
   | DepositedCash evt -> {
      props with
         Name = "Deposit Received"
         Info = "Deposit Received"
         MoneyFlow = Some MoneyFlow.In
         Source = Some evt.Data.Origin
         Destination = Some account.FullName
         Amount = Some <| Money.format evt.Data.Amount
     }
   | DebitedAccount evt ->
      let employee = evt.Data.EmployeePurchaseReference
      // TODO: rename Origin field as Merchant
      let employee =
         $"{employee.EmployeeName}**{employee.EmployeeCardNumberLast4}"

      {
         props with
            Name = "Purchase"
            Info = $"Purchase from {evt.Data.Origin} by {employee}"
            Amount = Some <| Money.format evt.Data.Amount
            MoneyFlow = Some MoneyFlow.Out
            Source = Some $"{account.FullName} via {employee}"
            Destination = Some evt.Data.Origin
      }
   | MaintenanceFeeDebited evt -> {
      props with
         Info = "Maintenance Fee"
         Amount = Some <| Money.format evt.Data.Amount
         MoneyFlow = Some MoneyFlow.Out
     }
   | MaintenanceFeeSkipped _ -> {
      props with
         Info = "Skipped Maintenance Fee"
     }
   | DomesticTransferRecipient evt -> {
      props with
         Name = "Created Domestic Recipient"
         Info =
            $"Created domestic recipient: {domesticRecipientName evt.Data.Recipient}"
     }
   | EditedDomesticTransferRecipient evt -> {
      props with
         Name = "Edited Domestic Recipient"
         Info = $"Edited recipient: {domesticRecipientName evt.Data.Recipient}"
     }
   | InternalTransferWithinOrgPending evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Internal Transfer"
            Info =
               $"Moved money from {info.Sender.Name} to {info.Recipient.Name}"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.Out
            Source = Some account.FullName
            Destination = Some info.Recipient.Name
      }
   | InternalTransferWithinOrgApproved evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Internal Transfer Approved"
            Info =
               $"Approved money movement from {info.Sender.Name} to {info.Recipient.Name}"
            Amount = Some <| Money.format info.Amount
      }
   | InternalTransferWithinOrgRejected evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Internal Transfer Rejected"
            Info =
               $"Declined money movement from {info.Sender.Name} to {info.Recipient.Name} 
              - Reason: {evt.Data.Reason} 
              - Account refunded"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.In
      }
   | InternalTransferBetweenOrgsPending evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Transfer Between Orgs"
            Info =
               $"Transferred from {info.Sender.Name} to {info.Recipient.Name}"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.Out
            Source = Some account.FullName
            Destination = Some info.Recipient.Name
      }
   | InternalTransferBetweenOrgsScheduled evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Transfer Between Orgs Scheduled"
            Info =
               $"Transfer to {info.Recipient.Name} scheduled for {DateTime.formatShort info.ScheduledDate}"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = None
            Source = Some account.FullName
            Destination = Some info.Recipient.Name
      }
   | InternalTransferBetweenOrgsApproved evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Transfer Between Orgs Approved"
            Info =
               $"Approved transfer from {info.Sender.Name} to {info.Recipient.Name}"
            Amount = Some <| Money.format info.Amount
      }
   | InternalTransferBetweenOrgsRejected evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Transfer Between Orgs Rejected"
            Info =
               $"Declined transfer from {info.Sender.Name} to {info.Recipient.Name} 
              - Reason: {evt.Data.Reason} 
              - Account refunded"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.In
      }
   | DomesticTransferPending evt ->
      let info = evt.Data.BaseInfo
      let recipientName = domesticRecipientName info.Recipient

      {
         props with
            Name = "Domestic Transfer"
            Info = $"Domestic transfer processing to {recipientName}"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.Out
            Source = Some account.FullName
            Destination = Some recipientName
      }
   | DomesticTransferScheduled evt ->
      let info = evt.Data.BaseInfo

      let recipientName = domesticRecipientName info.Recipient

      {
         props with
            Name = "Domestic Transfer"
            Info =
               $"Domestic transfer to {recipientName} scheduled for {DateTime.formatShort info.ScheduledDate}"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = None
            Source = Some account.FullName
            Destination = Some recipientName
      }
   | DomesticTransferApproved evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Domestic Transfer Approved"
            Info =
               $"Domestic transfer approved to {domesticRecipientName info.Recipient}"
            Amount = Some <| Money.format info.Amount
      }
   | DomesticTransferRejected evt ->
      let info = evt.Data.BaseInfo

      let recipientName = domesticRecipientName info.Recipient

      {
         props with
            Name = "Domestic Transfer Declined"
            Info =
               $"Domestic transfer declined to {recipientName} 
                 - Reason {evt.Data.Reason} 
                 - Account refunded"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.In
            Source = Some account.FullName
            Destination = Some recipientName
      }
   | DomesticTransferProgress evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Info =
               $"Progress update received for domestic transfer 
                 to {domesticRecipientName info.Recipient}
                 - {evt.Data.InProgressInfo}"
            Amount = Some <| Money.format info.Amount
      }
   | InternalTransferWithinOrgDeposited evt ->
      let info = evt.Data.BaseInfo
      let sender = info.Sender.Name

      {
         props with
            Name = "Transfer Deposit Within Org"
            Info = $"Received transfer deposit from {sender}."
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.In
            Source = Some sender
            Destination = Some account.FullName
      }
   | InternalTransferBetweenOrgsDeposited evt ->
      let info = evt.Data.BaseInfo
      let sender = info.Sender.Name

      {
         props with
            Name = "Transfer Deposit Between Orgs"
            Info = $"Received transfer deposit from {sender}."
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.In
            Source = Some sender
            Destination = Some account.FullName
      }
   | BillingCycleStarted _ -> {
      props with
         Info =
            "New billing cycle. Transactions consolidated into billing statement."
     }
   | AccountClosed evt -> {
      props with
         Info = $"Closed Account - Reference: {evt.Data.Reference}"
     }
   | RecipientNicknamed evt -> {
      props with
         Info =
            match evt.Data.Nickname with
            | None -> "Removed recipient nickname."
            | Some name -> $"Updated recipient nickname to {name}"
     }
   | PlatformPaymentRequested evt ->
      let p = evt.Data.BaseInfo

      {
         props with
            Name = "Payment Requested"
            Info = $"Requested payment from {p.Payer.OrgName}"
            Amount = Some <| Money.format p.Amount
            MoneyFlow = None
            Source = Some p.Payer.OrgName
            Destination = Some account.FullName
      }
   | PlatformPaymentPaid evt ->
      let p = evt.Data.BaseInfo

      {
         props with
            Name = "Payment Sent"
            Info = $"Sent payment to {p.Payee.OrgName}."
            Amount = Some <| Money.format p.Amount
            MoneyFlow = Some MoneyFlow.Out
            Source = Some account.FullName
            Destination = Some p.Payee.OrgName
      }
   | PlatformPaymentDeposited evt ->
      let p = evt.Data.BaseInfo

      {
         props with
            Name = "Payment"
            Info = $"Received payment from {p.Payer.OrgName}."
            Amount = Some <| Money.format p.Amount
            MoneyFlow = Some MoneyFlow.In
            Source = Some p.Payer.OrgName
            Destination = Some account.FullName
      }
   | PlatformPaymentCancelled evt ->
      let p = evt.Data.BaseInfo

      {
         props with
            Name = "Payment Cancelled"
            Info = $"Cancelled payment request to {p.Payer.OrgName}"
            Amount = Some <| Money.format p.Amount
            MoneyFlow = None
      }
   | PlatformPaymentDeclined evt ->
      let p = evt.Data.BaseInfo

      {
         props with
            Name = "Payment Declined"
            Info = $"{p.Payer.OrgName} declined payment request"
            Amount = Some <| Money.format p.Amount
            MoneyFlow = None
      }
   | InternalAutomatedTransferPending evt ->
      let info = evt.Data.BaseInfo

      let reason =
         match evt.Data.Rule with
         | AutomaticTransfer.AutomaticTransferRule.ZeroBalance _ ->
            "Maintain a zero balance."
         | AutomaticTransfer.AutomaticTransferRule.TargetBalance rule ->
            let targetBalance =
               rule.TargetAccountBalance
               |> PositiveAmount.get
               |> Money.formatShort

            let msg = $"Restore balance to {targetBalance}."

            match rule.TargetBalanceRange with
            | None -> msg
            | Some range ->
               let low =
                  range.LowerBound |> PositiveAmount.get |> Money.formatShort

               let upper =
                  range.UpperBound |> PositiveAmount.get |> Money.formatShort

               $"{msg} Balance out of range ({low}, {upper})"
         | AutomaticTransfer.AutomaticTransferRule.PercentDistribution rule ->
            let rule = AutomaticTransfer.PercentDistributionRule.get rule
            $"Maintain a zero balance by distributing it among {rule.DestinationAccounts.Length} accounts."

      {
         props with
            Name = "Internal Automated Transfer"
            Info =
               $"Auto Balance Management: moved money from {info.Sender.Name} to {info.Recipient.Name}
               - Reason: {reason}"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.Out
            Source = Some account.FullName
            Destination = Some info.Recipient.Name
      }
   | InternalAutomatedTransferApproved evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Internal Automated Transfer Approved"
            Info =
               $"Auto Balance Management: approve transfer from {info.Sender.Name} to {info.Recipient.Name}"
            Amount = Some <| Money.format info.Amount
      }
   | InternalAutomatedTransferRejected evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Name = "Internal Automated Transfer Rejected"
            Info =
               $"Auto Balance Management: declined transfer from {info.Sender.Name} to {info.Recipient.Name} 
              - Reason: {evt.Data.Reason} 
              - Account refunded"
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.In
      }
   | InternalAutomatedTransferDeposited evt ->
      let info = evt.Data.BaseInfo
      let sender = info.Sender.Name

      {
         props with
            Name = "Internal Automated Transfer Deposit"
            Info =
               $"Auto Balance Management: received transfer deposit from {sender}."
            Amount = Some <| Money.format info.Amount
            MoneyFlow = Some MoneyFlow.In
            Source = Some sender
            Destination = Some account.FullName
      }

type SelectedCard = { Display: string; CardId: CardId }

[<RequireQualifiedAccess>]
type AccountActionView =
   | Debit
   | Deposit
   | Transfer of (RecipientAccountEnvironment * AccountId) option
   | RegisterTransferRecipient
   | EditTransferRecipient of AccountId

type AccountBrowserQuery = {
   MoneyFlow: MoneyFlow option
   Category: CategoryFilter option
   Amount: AmountFilter option
   Date: DateFilter option
   Action: AccountActionView option
   Transaction: EventId option
   SelectedCards: (SelectedCard list) option
   SelectedInitiatedBy: (UIDomain.Employee.SelectedEmployee list) option
   EventType: (TransactionGroupFilter list) option
} with

   member x.ChangeDetection =
      Serialization.serialize {|
         MoneyFlow = x.MoneyFlow
         Category = x.Category
         Amount = x.Amount
         Date = x.Date
         CardIds = x.SelectedCards
         InitiatedByIds = x.SelectedInitiatedBy
         EventType = x.EventType
      |}

module AccountBrowserQuery =
   let toQueryParams (query: AccountBrowserQuery) : (string * string) list =
      let agg =
         query.Amount
         |> Option.map AmountFilter.toQuery
         |> Option.defaultValue []

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
            ("categoryIds", listToQueryString catIds) :: agg
         | Some(CategoryFilter.IsCategorized isCat) ->
            ("isCategorized", string isCat) :: agg
         | None -> agg

      let agg =
         match query.Action with
         | Some(AccountActionView.EditTransferRecipient accountId) ->
            [
               "action", "EditTransferRecipient"
               "transferRecipient", string accountId
            ]
            @ agg
         | Some(AccountActionView.Transfer qParamsOpt) ->
            match qParamsOpt with
            | None -> ("action", "Transfer") :: agg
            | Some(accountEnvironment, recipientId) ->
               [
                  "action", "Transfer"
                  "accountEnvironment", string accountEnvironment
                  "transferRecipient", string recipientId
               ]
               @ agg
         | Some view -> ("action", string view) :: agg
         | None -> agg

      let agg =
         match query.SelectedCards with
         | None -> agg
         | Some cards -> ("cards", Serialization.serialize cards) :: agg

      let agg =
         match query.SelectedInitiatedBy with
         | None -> agg
         | Some initiatedBy ->
            ("initiatedBy", Serialization.serialize initiatedBy) :: agg

      let agg =
         match query.EventType with
         | None -> agg
         | Some filters -> ("events", listToQueryString filters) :: agg

      match query.Transaction with
      | Some txnId -> ("transaction", string txnId) :: agg
      | None -> agg

   let fromQueryParams
      (queryParams: (string * string) list)
      : AccountBrowserQuery
      =
      let queryParams = Map.ofList queryParams

      {
         Category =
            Map.tryFind "categoryIds" queryParams
            |> Option.bind CategoryFilter.categoryFromQueryString
            |> Option.orElse (
               Map.tryFind "isCategorized" queryParams
               |> Option.map (bool.Parse >> CategoryFilter.IsCategorized)
            )
         Amount = AmountFilter.fromQueryString queryParams
         MoneyFlow =
            Map.tryFind "moneyFlow" queryParams
            |> Option.bind MoneyFlow.fromString
         Date =
            Map.tryFind "date" queryParams |> Option.bind DateFilter.fromString
         Action =
            Map.tryFind "action" queryParams
            |> Option.bind (function
               | "Deposit" -> Some AccountActionView.Deposit
               | "Debit" -> Some AccountActionView.Debit
               | "Transfer" ->
                  let envOpt =
                     Map.tryFind "accountEnvironment" queryParams
                     |> Option.bind RecipientAccountEnvironment.fromString

                  let recipientIdOpt =
                     Map.tryFind "transferRecipient" queryParams
                     |> Option.bind Guid.parseOptional

                  Option.map2
                     (fun env recipientId ->
                        AccountActionView.Transfer(
                           Some(env, AccountId recipientId)
                        ))
                     envOpt
                     recipientIdOpt
                  |> Option.orElse (Some(AccountActionView.Transfer None))
               | "RegisterTransferRecipient" ->
                  Some AccountActionView.RegisterTransferRecipient
               | "EditTransferRecipient" ->
                  Map.tryFind "transferRecipient" queryParams
                  |> Option.map (
                     Guid.Parse
                     >> AccountId
                     >> AccountActionView.EditTransferRecipient
                  )
               | view ->
                  Log.error $"Account action view not implemented: {view}"
                  None)
         Transaction =
            Map.tryFind "transaction" queryParams
            |> Option.bind (Guid.parseOptional >> Option.map EventId)
         SelectedCards =
            queryParams
            |> Map.tryFind "cards"
            |> Option.bind (
               Serialization.deserialize<SelectedCard list> >> Result.toOption
            )
         SelectedInitiatedBy =
            Map.tryFind "initiatedBy" queryParams
            |> Option.bind UIDomain.Employee.parseEmployees
         EventType =
            Map.tryFind "events" queryParams
            |> Option.bind TransactionGroupFilter.fromQueryString
      }

   let empty: AccountBrowserQuery = {
      Category = None
      MoneyFlow = None
      Amount = None
      Date = None
      Action = None
      Transaction = None
      SelectedCards = None
      SelectedInitiatedBy = None
      EventType = None
   }

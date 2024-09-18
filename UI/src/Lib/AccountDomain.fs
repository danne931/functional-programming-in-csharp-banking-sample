module UIDomain.Account

open System

open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes
open Lib.NetworkQuery

type AccountProfilesMaybe = Result<Map<AccountId, AccountProfile> option, Err>

type AccountAndTransactionsMaybe =
   Result<(Account * AccountEvent list) option, Err>

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

let nameAndNicknamePair
   (account: Account)
   (recipientId: AccountId)
   : string * string option
   =
   let recipient = Map.tryFind recipientId account.DomesticTransferRecipients

   match recipient with
   | Some recipient -> recipient.Name, recipient.Nickname
   | None -> "", None

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

   let accountName = account.Name + " **" + account.AccountNumber.Last4

   let recipientName (recipientId: AccountId) =
      let name, nickname = nameAndNicknamePair account recipientId
      nickname |> Option.defaultValue name

   let recipientNameAndAccountNumber
      (recipientId: AccountId)
      (recipientAccountNum: AccountNumber)
      =
      let name = recipientName recipientId
      name + " **" + recipientAccountNum.Last4

   match txn with
   | CreatedAccount _ -> { props with Info = "Created Account" }
   | DepositedCash evt -> {
      props with
         Name = "Deposit Received"
         Info = "Deposit Received"
         MoneyFlow = Some MoneyFlow.In
         Source = Some evt.Data.Origin
         Destination = Some accountName
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
            Source = Some $"{accountName} via {employee}"
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
   | DomesticTransferRecipient evt ->
      let recipientName =
         recipientNameAndAccountNumber
            evt.Data.Recipient.AccountId
            evt.Data.Recipient.AccountNumber

      {
         props with
            Name = "Created Domestic Recipient"
            Info = $"Created domestic recipient: {recipientName}"
      }
   | EditedDomesticTransferRecipient evt ->
      let recipientName =
         recipientNameAndAccountNumber
            evt.Data.Recipient.AccountId
            evt.Data.Recipient.AccountNumber

      {
         props with
            Name = "Edited Domestic Recipient"
            Info = $"Edited recipient: {recipientName}"
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
            Source = Some accountName
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
            Source = Some accountName
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

      let recipientName =
         recipientNameAndAccountNumber
            info.Recipient.AccountId
            info.Recipient.AccountNumber

      {
         props with
            Name = "Domestic Transfer"
            Info = $"Domestic transfer processing to {recipientName}"
            Amount = Some <| Money.format evt.Data.BaseInfo.Amount
            MoneyFlow = Some MoneyFlow.Out
            Source = Some accountName
            Destination = Some recipientName
      }
   | DomesticTransferApproved evt ->
      let info = evt.Data.BaseInfo

      let recipientName =
         recipientNameAndAccountNumber
            info.Recipient.AccountId
            info.Recipient.AccountNumber

      {
         props with
            Name = "Domestic Transfer Approved"
            Info = $"Domestic transfer approved to {recipientName}"
            Amount = Some <| Money.format evt.Data.BaseInfo.Amount
      }
   | DomesticTransferRejected evt ->
      let info = evt.Data.BaseInfo

      let recipientName =
         recipientNameAndAccountNumber
            info.Recipient.AccountId
            info.Recipient.AccountNumber

      {
         props with
            Name = "Domestic Transfer Declined"
            Info =
               $"Domestic transfer declined to {recipientName} 
                 - Reason {evt.Data.Reason} 
                 - Account refunded"
            Amount = Some <| Money.format evt.Data.BaseInfo.Amount
            MoneyFlow = Some MoneyFlow.In
            Source = Some accountName
            Destination = Some recipientName
      }
   | DomesticTransferProgress evt ->
      let info = evt.Data.BaseInfo

      {
         props with
            Info =
               $"Progress update received for domestic transfer 
                 to {recipientName info.Recipient.AccountId}
                 - {evt.Data.InProgressInfo}"
            Amount = Some <| Money.format evt.Data.BaseInfo.Amount
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
            Destination = Some accountName
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
            Destination = Some accountName
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
            Destination = Some accountName
      }
   | PlatformPaymentPaid evt ->
      let p = evt.Data.BaseInfo

      {
         props with
            Name = "Payment Sent"
            Info = $"Sent payment to {p.Payee.OrgName}."
            Amount = Some <| Money.format p.Amount
            MoneyFlow = Some MoneyFlow.Out
            Source = Some accountName
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
            Destination = Some accountName
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

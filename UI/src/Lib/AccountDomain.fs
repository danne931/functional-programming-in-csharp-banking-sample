module Bank.Account.UIDomain

open System

open AsyncUtil
open Bank.Account.Domain
open Lib.SharedTypes

type AccountsMaybe = Result<Map<Guid, AccountState> option, Err>
type AccountMaybe = Result<AccountState option, Err>

type AccountTransform = AccountState -> AccountState

let accountsFromDeferred
   (deferred: Deferred<AccountsMaybe>)
   : Map<Guid, AccountState> option
   =
   match deferred with
   | Deferred.Resolved(Ok(Some accounts)) -> Some accounts
   | _ -> None

let findAccount
   (deferred: Deferred<AccountsMaybe>)
   (accountId: Guid)
   : AccountState option
   =
   accountsFromDeferred deferred |> Option.bind (Map.tryFind accountId)

let updateAccount
   (transform: AccountTransform)
   (deferred: Deferred<AccountsMaybe>)
   (accountId: Guid)
   : Deferred<AccountsMaybe>
   =
   deferred
   |> Deferred.map (fun accountsMaybe ->
      match accountsMaybe with
      | Ok(Some accounts) ->
         accounts |> Map.change accountId (Option.map transform) |> Some |> Ok
      | other -> other)

[<RequireQualifiedAccess>]
type MoneyFlow =
   | None
   | In
   | Out

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
}

let transactionUIFriendly (txn: AccountEvent) : TransactionUIFriendly =
   let _, envelope = AccountEnvelope.unwrap txn

   let props = {
      DateNaked = envelope.Timestamp
      Date = time.formatDate envelope.Timestamp
      Name = envelope.EventName
      Origin = None
      AmountNaked = None
      Amount = None
      Sign = ""
      Info = None
      MoneyFlow = MoneyFlow.None
   }

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
        }
      | DebitedAccount evt -> {
         props with
            Name = "Debit"
            AmountNaked = Some evt.Data.DebitedAmount
            Origin = Some evt.Data.Origin
            MoneyFlow = MoneyFlow.Out
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
            Info = Some $"Recipient: {evt.Data.Recipient.Name}"
            AmountNaked = Some evt.Data.DebitedAmount
            MoneyFlow = MoneyFlow.Out
        }
      | TransferProgress evt -> {
         props with
            Name = "Transfer Progress Update"
            Info =
               Some
                  $"Status {evt.Data.Status} Recipient: {evt.Data.Recipient.Name}"
            AmountNaked = Some evt.Data.DebitedAmount
        }
      | TransferApproved evt -> {
         props with
            Name = "Transfer Approved"
            Info = Some $"Recipient: {evt.Data.Recipient.Name}"
            AmountNaked = Some evt.Data.DebitedAmount
        }
      | TransferRejected evt -> {
         props with
            Name = "Transfer Rejected"
            Info =
               Some
                  $"Recipient: {evt.Data.Recipient.Name} - Reason {evt.Data.Reason} - Acount refunded"
            AmountNaked = Some evt.Data.DebitedAmount
            MoneyFlow = MoneyFlow.In
        }
      | TransferDeposited evt -> {
         props with
            Name = "Transfer Received"
            AmountNaked = Some evt.Data.DepositedAmount
            Origin = Some evt.Data.Origin
            MoneyFlow = MoneyFlow.In
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

   let sign =
      match props.MoneyFlow with
      | MoneyFlow.In -> "+"
      | MoneyFlow.Out -> "-"
      | MoneyFlow.None -> props.Sign

   let currencySymbol = "$"

   {
      props with
         Sign = sign
         Amount =
            props.AmountNaked
            |> Option.map (fun amount -> $"{sign}{currencySymbol}{amount}")
   }

type PotentialInternalTransferRecipients =
   private | PotentialInternalTransferRecipients of Map<Guid, AccountState>

module PotentialInternalTransferRecipients =
   let create (account: AccountState) (accounts: Map<Guid, AccountState>) =
      let potentialRecipients =
         accounts
         |> Map.filter (fun id _ ->
            let accountInRecipients =
               account.TransferRecipients |> Map.containsKey (string id)

            id <> account.EntityId && not accountInRecipients)

      PotentialInternalTransferRecipients potentialRecipients

   let value (PotentialInternalTransferRecipients recipients) = recipients

module BankTypes

open System
open System.Threading.Tasks

open Lib.Types
open Bank.Account.Domain
open Bank.Transfer.Domain

type AccountEvent =
   | CreatedAccount of BankEvent<CreatedAccount>
   | DepositedCash of BankEvent<DepositedCash>
   | DebitedAccount of BankEvent<DebitedAccount>
   | DailyDebitLimitUpdated of BankEvent<DailyDebitLimitUpdated>
   | LockedCard of BankEvent<LockedCard>
   | UnlockedCard of BankEvent<UnlockedCard>
   | TransferPending of BankEvent<TransferPending>
   | TransferApproved of BankEvent<TransferApproved>
   | TransferRejected of BankEvent<TransferRejected>
   | InternalTransferRecipient of BankEvent<RegisteredInternalTransferRecipient>
   | DomesticTransferRecipient of BankEvent<RegisteredDomesticTransferRecipient>
   | InternationalTransferRecipient of
      BankEvent<RegisteredInternationalTransferRecipient>

type OpenEventEnvelope = AccountEvent * Envelope

[<RequireQualifiedAccess>]
module Envelope =
   let private get (evt: BankEvent<'E>) = {
      EntityId = evt.EntityId
      Timestamp = evt.Timestamp
      EventName = evt.EventName
      CorrelationId = evt.CorrelationId
   }

   let bind (transformer: obj -> 't) (evt: AccountEvent) =
      match evt with
      | CreatedAccount evt -> transformer evt
      | DepositedCash evt -> transformer evt
      | DebitedAccount evt -> transformer evt
      | DailyDebitLimitUpdated evt -> transformer evt
      | LockedCard evt -> transformer evt
      | UnlockedCard evt -> transformer evt
      | InternalTransferRecipient evt -> transformer evt
      | DomesticTransferRecipient evt -> transformer evt
      | InternationalTransferRecipient evt -> transformer evt
      | TransferPending evt -> transformer evt
      | TransferApproved evt -> transformer evt
      | TransferRejected evt -> transformer evt

   let wrap (o: obj) : AccountEvent =
      match o with
      | :? BankEvent<CreatedAccount> as evt -> evt |> CreatedAccount
      | :? BankEvent<DepositedCash> as evt -> evt |> DepositedCash
      | :? BankEvent<DebitedAccount> as evt -> evt |> DebitedAccount
      | :? BankEvent<DailyDebitLimitUpdated> as evt ->
         evt |> DailyDebitLimitUpdated
      | :? BankEvent<LockedCard> as evt -> evt |> LockedCard
      | :? BankEvent<UnlockedCard> as evt -> evt |> UnlockedCard
      | :? BankEvent<RegisteredInternalTransferRecipient> as evt ->
         evt |> InternalTransferRecipient
      | :? BankEvent<RegisteredDomesticTransferRecipient> as evt ->
         evt |> DomesticTransferRecipient
      | :? BankEvent<RegisteredInternationalTransferRecipient> as evt ->
         evt |> InternationalTransferRecipient
      | :? BankEvent<TransferPending> as evt -> evt |> TransferPending
      | :? BankEvent<TransferApproved> as evt -> evt |> TransferApproved
      | :? BankEvent<TransferRejected> as evt -> evt |> TransferRejected

   let unwrap (o: AccountEvent) : OpenEventEnvelope =
      match o with
      | CreatedAccount evt -> (wrap evt, get evt)
      | DepositedCash evt -> (wrap evt, get evt)
      | DebitedAccount evt -> (wrap evt, get evt)
      | DailyDebitLimitUpdated evt -> (wrap evt, get evt)
      | LockedCard evt -> (wrap evt, get evt)
      | UnlockedCard evt -> (wrap evt, get evt)
      | InternalTransferRecipient evt -> (wrap evt, get evt)
      | DomesticTransferRecipient evt -> (wrap evt, get evt)
      | InternationalTransferRecipient evt -> (wrap evt, get evt)
      | TransferPending evt -> (wrap evt, get evt)
      | TransferApproved evt -> (wrap evt, get evt)
      | TransferRejected evt -> (wrap evt, get evt)

type AccountStatus =
   | Active = 0
   | ActiveWithLockedCard = 1
   | Closed = 2

type AccountState =
   {
      EntityId: Guid
      FirstName: string
      LastName: string
      Currency: string
      Status: AccountStatus
      Balance: decimal
      AllowedOverdraft: decimal
      DailyDebitLimit: decimal
      DailyDebitAccrued: decimal
      LastDebitDate: DateTime option
      TransferRecipients: Map<string, TransferRecipient>
   }

   member x.FullName = $"{x.FirstName} {x.LastName}"

type AccountCoordinatorMessage =
   | InitAccount of AccountState
   | StateChange of Command
   | Delete of Guid

type AccountMessage =
   | StartChildren of Guid
   | Lookup of Guid
   | StateChange of Command

type AccountPersistence = {
   loadAccountEvents: Guid -> AccountEvent list option Task
   loadAccount: Guid -> AccountState option Task
   save: OpenEventEnvelope -> unit Task
}

type AccountBroadcast = {
   broadcast: AccountEvent * AccountState -> Task
   broadcastError: string -> Task
}

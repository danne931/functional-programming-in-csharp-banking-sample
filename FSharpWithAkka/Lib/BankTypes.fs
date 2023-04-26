module BankTypes

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
   | DebitedTransfer of BankEvent<DebitedTransfer>
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
      | DebitedTransfer evt -> transformer evt

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
      | :? BankEvent<DebitedTransfer> as evt -> evt |> DebitedTransfer

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
      | DebitedTransfer evt -> (wrap evt, get evt)
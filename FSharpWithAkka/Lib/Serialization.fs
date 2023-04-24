[<RequireQualifiedAccess>]
module Serialization

open System.Text.Json
open System.Text.Json.Serialization

open BankTypes
open Lib.Types
open Bank.Account.Domain
open Bank.Transfer.Domain

let jsonOptions = JsonFSharpOptions.Default().ToJsonSerializerOptions()
jsonOptions.Converters.Add(JsonStringEnumConverter())

let private eventTypeMapping =
   Map [
      nameof CreatedAccount, typeof<BankEvent<CreatedAccount>>
      nameof DebitedTransfer, typeof<BankEvent<DebitedTransfer>>
      nameof DebitedAccount, typeof<BankEvent<DebitedAccount>>
      nameof DailyDebitLimitUpdated, typeof<BankEvent<DailyDebitLimitUpdated>>
      nameof DepositedCash, typeof<BankEvent<DepositedCash>>
      nameof LockedCard, typeof<BankEvent<LockedCard>>
      nameof UnlockedCard, typeof<BankEvent<UnlockedCard>>

      nameof RegisteredInternalTransferRecipient,
      typeof<BankEvent<RegisteredInternalTransferRecipient>>

      nameof RegisteredDomesticTransferRecipient,
      typeof<BankEvent<RegisteredDomesticTransferRecipient>>

      nameof RegisteredInternationalTransferRecipient,
      typeof<BankEvent<RegisteredInternationalTransferRecipient>>
   ]

let serialize (evt: AccountEvent) =
   Envelope.bind
      (fun e -> JsonSerializer.SerializeToUtf8Bytes(e, jsonOptions))
      evt

let deserialize (data: byte array) (eventName: string) =
   let deserialized =
      JsonSerializer.Deserialize(data, eventTypeMapping[eventName], jsonOptions)

   let (event, _) = Envelope.unwrap deserialized
   event

[<RequireQualifiedAccess>]
module Serialization

open System.Text.Json
open System.Text.Json.Serialization

open BankTypes
open Lib.Types
open Bank.Account.Domain
open Bank.Transfer.Domain

let private baseConfig =
   JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags()

let mergeDefaultJsonOptions (options: JsonSerializerOptions) =
   options.Converters.Add(JsonStringEnumConverter())
   options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase

let jsonOptions = baseConfig.ToJsonSerializerOptions()
mergeDefaultJsonOptions jsonOptions

let withInjectedOptions opts =
   mergeDefaultJsonOptions opts
   baseConfig.AddToJsonSerializerOptions opts

let private eventTypeMapping =
   Map [
      nameof CreatedAccount, typeof<BankEvent<CreatedAccount>>
      nameof TransferPending, typeof<BankEvent<TransferPending>>
      nameof TransferApproved, typeof<BankEvent<TransferApproved>>
      nameof TransferRejected, typeof<BankEvent<TransferRejected>>
      nameof DebitedAccount, typeof<BankEvent<DebitedAccount>>
      nameof MaintenanceFeeDebited, typeof<BankEvent<MaintenanceFeeDebited>>
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

let serializeEvent (evt: AccountEvent) =
   Envelope.bind
      (fun e -> JsonSerializer.SerializeToUtf8Bytes(e, jsonOptions))
      evt

let deserializeEvent (data: byte[]) (eventName: string) =
   let deserialized =
      JsonSerializer.Deserialize(data, eventTypeMapping[eventName], jsonOptions)

   let (event, _) = deserialized |> Envelope.wrap |> Envelope.unwrap
   event

let deserialize<'t> (data: string) : Result<'t, string> =
   try
      JsonSerializer.Deserialize<'t>(data) |> Ok
   with err when true ->
      $"Deserialization error: {err.Message}" |> Error

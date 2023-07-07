namespace Bank.Transfer.Domain

open FSharp.Control
open Microsoft.FSharp.Core.Option

open Lib.Types

module Validators =
   let transfer () =
      (fun (cmd: TransferCommand) ->
         if cmd.Amount <= 0m then
            Error "InvalidTransferAmount"
         else
            Ok())
      |> Validator

   let registerTransferRecipient () =
      (fun (cmd: RegisterTransferRecipientCommand) ->
         let recipient = cmd.Recipient

         if
            recipient.AccountEnvironment = RecipientAccountEnvironment.Domestic
            && isNone recipient.RoutingNumber
         then
            Error "TransferErr.InvalidDomesticRecipient"
         elif
            recipient.AccountEnvironment = RecipientAccountEnvironment.International
            && isNull recipient.Currency
         then
            Error "TransferErr.InvalidInternationalRecipient"
         else
            Ok())
      |> Validator

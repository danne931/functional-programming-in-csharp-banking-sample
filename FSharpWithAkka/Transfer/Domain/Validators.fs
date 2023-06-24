namespace Bank.Transfer.Domain

open System.Threading.Tasks
open EventStore.Client
open FSharp.Control
open Microsoft.FSharp.Core.Option

open Lib.Types

module Validators =
   let transfer () =
      (fun (cmd: TransferCommand) ->
         if cmd.Amount <= 0 then
            Error "InvalidTransferAmount"
         else
            Ok())
      |> Validator

   let registerTransferRecipient
      (
         es: EventStoreClient,
         recipientExists: EventStoreClient -> TransferRecipient -> bool Task
      )
      =
      (fun (cmd: RegisterTransferRecipientCommand) -> task {
         let recipient = cmd.Recipient

         if
            recipient.AccountEnvironment = RecipientAccountEnvironment.Domestic
            && isNone recipient.RoutingNumber
         then
            return Error "TransferErr.InvalidDomesticRecipient"
         elif
            recipient.AccountEnvironment = RecipientAccountEnvironment.International
            && isNull recipient.Currency
         then
            return Error "TransferErr.InvalidInternationalRecipient"
         else
            let! exists = recipientExists es recipient

            // TODO: Remove Guid play
            if not exists then
               return
                  Error
                     $"TransferErr.RecipientNotFound(Guid(recipient.Identification))"
            else
               return Ok()
      })
      |> AsyncValidator

namespace Bank.Transfer.Domain

open Microsoft.FSharp.Core.Option

module Validators =
   let transfer () =
      (fun (cmd: TransferCommand) ->
         if cmd.Amount <= 0m then
            Error "InvalidTransferAmount"
         else
            Ok cmd)

   let registerTransferRecipient () =
      (fun (cmd: RegisterTransferRecipientCommand) ->
         let recipient = cmd.Recipient

         if string cmd.EntityId = recipient.Identification then
            Error "CanNotRegisterSelfAsRecipient"
         elif
            recipient.AccountEnvironment = RecipientAccountEnvironment.Domestic
            && isNone recipient.RoutingNumber
         then
            Error "InvalidDomesticRecipient"
         elif
            recipient.AccountEnvironment = RecipientAccountEnvironment.International
            && isNull recipient.Currency
         then
            Error "InvalidInternationalRecipient"
         else
            Ok cmd)

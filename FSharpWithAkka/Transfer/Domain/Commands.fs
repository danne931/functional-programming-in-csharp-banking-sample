namespace Bank.Transfer.Domain

open System

open Lib.Types

type AckReceipt = string

type RecipientAccountEnvironment =
   | Internal = 0
   | Domestic = 1
   | International = 2

type RecipientAccountIdentificationStrategy =
   | AccountId = 0
   | SwiftBIC = 1
   | IBAN = 2
   | NationalID = 3

type TransferRecipient = {
   LastName: string
   FirstName: string
   Identification: string
   AccountEnvironment: RecipientAccountEnvironment
   IdentificationStrategy: RecipientAccountIdentificationStrategy
   RoutingNumber: string option
   Currency: string
}

type TransferCommand
   (
      entityId,
      correlationId,
      recipient: TransferRecipient,
      date: DateTime,
      amount: decimal,
      reference: string
   ) =
   inherit Command(entityId, correlationId)
   member x.Recipient = recipient
   member x.Date = date
   member x.Amount = amount
   member x.Reference = reference

type ApproveTransferCommand
   (
      entityId,
      correlationId,
      recipient: TransferRecipient,
      date: DateTime,
      amount: decimal,
      ackReceipt: AckReceipt
   ) =
   inherit Command(entityId, correlationId)
   member x.Recipient = recipient
   member x.Date = date
   member x.Amount = amount
   member x.AckReceipt = ackReceipt

type RejectTransferCommand
   (
      entityId,
      correlationId,
      recipient: TransferRecipient,
      date: DateTime,
      amount: decimal,
      reason: string
   ) =
   inherit Command(entityId, correlationId)
   member x.Recipient = recipient
   member x.Date = date
   member x.Amount = amount
   member x.Reason = reason

type RegisterTransferRecipientCommand
   (entityId, recipient: TransferRecipient, correlationId) =
   inherit Command(entityId, correlationId)

   member x.Recipient = {
      recipient with
         Currency =
            if isNull recipient.Currency then
               "USD"
            else
               recipient.Currency
   }

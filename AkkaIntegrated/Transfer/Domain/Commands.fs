namespace Bank.Transfer.Domain

open System

open Lib.Types

type AckReceipt = string

type RecipientAccountEnvironment =
   | Internal
   | Domestic
   | International

type RecipientAccountIdentificationStrategy =
   | AccountId
   | SwiftBIC
   | IBAN
   | NationalID

type TransferRecipient = {
   LastName: string
   FirstName: string
   Identification: string
   AccountEnvironment: RecipientAccountEnvironment
   IdentificationStrategy: RecipientAccountIdentificationStrategy
   RoutingNumber: string option
   Currency: Currency
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
   member x.Recipient = recipient

type DepositTransferCommand
   (entityId, amount: decimal, origin: string, correlationId) =
   inherit Command(entityId, correlationId)
   member x.Amount = amount
   member x.Origin = origin

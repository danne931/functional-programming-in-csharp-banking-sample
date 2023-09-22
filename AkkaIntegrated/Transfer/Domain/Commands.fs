namespace Bank.Transfer.Domain

open System
open Validus

open Lib.Types
open Lib.Validators

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

   member x.toEvent() : ValidationResult<BankEvent<TransferPending>> = validate {
      let! _ = amountValidator "Transfer debit amount" x.Amount
      let! _ = dateNotDefaultValidator "Transfer date" x.Date

      return {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            Recipient = x.Recipient
            Date = x.Date
            DebitedAmount = x.Amount
            Reference =
               if String.IsNullOrEmpty x.Reference then
                  None
               else
                  Some x.Reference
         }
         CorrelationId = x.CorrelationId
      }
   }

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

   member x.toEvent() : ValidationResult<BankEvent<TransferApproved>> =
      Ok {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            Recipient = x.Recipient
            Date = x.Date
            DebitedAmount = x.Amount
            AckReceipt =
               if String.IsNullOrEmpty x.AckReceipt then
                  None
               else
                  Some x.AckReceipt
         }
         CorrelationId = x.CorrelationId
      }

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

   member x.toEvent() : ValidationResult<BankEvent<TransferRejected>> =
      Ok {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            Recipient = x.Recipient
            Date = x.Date
            DebitedAmount = x.Amount
            Reason = x.Reason
         }
         CorrelationId = x.CorrelationId
      }

type DepositTransferCommand
   (entityId, amount: decimal, origin: string, correlationId) =
   inherit Command(entityId, correlationId)
   member x.Amount = amount
   member x.Origin = origin

   member x.toEvent() : ValidationResult<BankEvent<TransferDeposited>> =
      Ok {
         EntityId = x.EntityId
         Timestamp = x.Timestamp
         Data = {
            DepositedAmount = x.Amount
            Origin = x.Origin
         }
         CorrelationId = x.CorrelationId
      }

type RegisterTransferRecipientCommand
   (entityId, recipient: TransferRecipient, correlationId) =
   inherit Command(entityId, correlationId)
   member x.Recipient = recipient

module TransferRecipientEvent =
   let recipientValidation (cmd: RegisterTransferRecipientCommand) = validate {
      let! _ =
         transferRecipientIdValidator
            (string cmd.EntityId)
            cmd.Recipient.Identification

      and! _ = accountNumberValidator cmd.Recipient.Identification

      and! _ = nameValidator "Recipient first name" cmd.Recipient.FirstName
      and! _ = nameValidator "Recipient last name" cmd.Recipient.LastName
      return cmd
   }

   let local
      (cmd: RegisterTransferRecipientCommand)
      : ValidationResult<BankEvent<RegisteredInternalTransferRecipient>>
      =
      validate {
         let! _ = recipientValidation cmd

         return {
            EntityId = cmd.EntityId
            Timestamp = cmd.Timestamp
            Data = {
               AccountNumber = cmd.Recipient.Identification
               LastName = cmd.Recipient.LastName
               FirstName = cmd.Recipient.FirstName
               Currency = cmd.Recipient.Currency
               AccountEnvironment = RecipientAccountEnvironment.Internal
            }
            CorrelationId = cmd.CorrelationId
         }
      }

   let domestic
      (cmd: RegisterTransferRecipientCommand)
      : ValidationResult<BankEvent<RegisteredDomesticTransferRecipient>>
      =
      validate {
         let! _ = recipientValidation cmd
         and! _ = routingNumberValidator cmd.Recipient.RoutingNumber

         return {
            EntityId = cmd.EntityId
            Timestamp = cmd.Timestamp
            Data = {
               LastName = cmd.Recipient.LastName
               FirstName = cmd.Recipient.FirstName
               AccountNumber = cmd.Recipient.Identification
               RoutingNumber = cmd.Recipient.RoutingNumber
               Currency = cmd.Recipient.Currency
               AccountEnvironment = RecipientAccountEnvironment.Domestic
            }
            CorrelationId = cmd.CorrelationId
         }
      }

   let international
      (cmd: RegisterTransferRecipientCommand)
      : ValidationResult<BankEvent<RegisteredInternationalTransferRecipient>>
      =
      Error
      <| ValidationErrors.create "Recipient" [
         "international not yet supported"
      ]

module TransferResponseToCommand =
   let approve (evt: BankEvent<TransferPending>) (ackReceipt: AckReceipt) =
      ApproveTransferCommand(
         evt.EntityId,
         evt.CorrelationId,
         evt.Data.Recipient,
         evt.Data.Date,
         evt.Data.DebitedAmount,
         ackReceipt
      )

   let reject (evt: BankEvent<TransferPending>) (reason: string) =
      RejectTransferCommand(
         evt.EntityId,
         evt.CorrelationId,
         evt.Data.Recipient,
         evt.Data.Date,
         evt.Data.DebitedAmount,
         reason
      )

namespace Bank.Transfer.Domain

open System

open Lib.Types

type TransferPending = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   Reference: string option
}

type TransferApproved = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   AckReceipt: AckReceipt option
}

type TransferRejected = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   Reason: string
}

module TransferEvent =
   let create (cmd: TransferCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         Recipient = cmd.Recipient
         Date = cmd.Date
         DebitedAmount = cmd.Amount
         Reference =
            if String.IsNullOrEmpty cmd.Reference then
               None
            else
               Some cmd.Reference
      }
      CorrelationId = cmd.CorrelationId
   }

   let approve (cmd: ApproveTransferCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         Recipient = cmd.Recipient
         Date = cmd.Date
         DebitedAmount = cmd.Amount
         AckReceipt =
            if String.IsNullOrEmpty cmd.AckReceipt then
               None
            else
               Some cmd.AckReceipt
      }
      CorrelationId = cmd.CorrelationId
   }

   let reject (cmd: RejectTransferCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         Recipient = cmd.Recipient
         Date = cmd.Date
         DebitedAmount = cmd.Amount
         Reason = cmd.Reason
      }
      CorrelationId = cmd.CorrelationId
   }

type RegisteredInternalTransferRecipient = {
   LastName: string
   FirstName: string
   AccountNumber: string
   Currency: Currency
   AccountEnvironment: RecipientAccountEnvironment
}

type RegisteredDomesticTransferRecipient = {
   LastName: string
   FirstName: string
   RoutingNumber: string option
   AccountNumber: string
   Currency: Currency
   AccountEnvironment: RecipientAccountEnvironment
}

type RegisteredInternationalTransferRecipient = {
   LastName: string
   FirstName: string
   Identification: string
   IdentificationStrategy: RecipientAccountIdentificationStrategy
   Currency: Currency
   AccountEnvironment: RecipientAccountEnvironment
}

module RegisterInternalTransferRecipientEvent =
   let create (cmd: RegisterTransferRecipientCommand) = {
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

module RegisterDomesticTransferRecipientEvent =
   let create (cmd: RegisterTransferRecipientCommand) = {
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

module RegisterInternationalTransferRecipientEvent =
   let create (cmd: RegisterTransferRecipientCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         LastName = cmd.Recipient.LastName
         FirstName = cmd.Recipient.FirstName
         Identification = cmd.Recipient.Identification
         IdentificationStrategy = cmd.Recipient.IdentificationStrategy
         Currency = cmd.Recipient.Currency
         AccountEnvironment = RecipientAccountEnvironment.International
      }
      CorrelationId = cmd.CorrelationId
   }


type TransferRecipientEvent =
   | RegisteredInternalTransferRecipient of
      BankEvent<RegisteredInternalTransferRecipient>
   | RegisteredDomesticTransferRecipient of
      BankEvent<RegisteredDomesticTransferRecipient>
   | RegisteredInternationalTransferRecipient of
      BankEvent<RegisteredInternationalTransferRecipient>

module RegisterTransferRecipientEvent =
   let eventToRecipient (evt: TransferRecipientEvent) =
      match evt with
      | RegisteredInternalTransferRecipient e -> {
         FirstName = e.Data.FirstName
         LastName = e.Data.LastName
         AccountEnvironment = e.Data.AccountEnvironment
         Identification = e.Data.AccountNumber
         IdentificationStrategy =
            RecipientAccountIdentificationStrategy.AccountId
         Currency = e.Data.Currency
         RoutingNumber = None
        }
      | RegisteredDomesticTransferRecipient e -> {
         FirstName = e.Data.FirstName
         LastName = e.Data.LastName
         AccountEnvironment = e.Data.AccountEnvironment
         Identification = e.Data.AccountNumber
         IdentificationStrategy =
            RecipientAccountIdentificationStrategy.AccountId
         Currency = e.Data.Currency
         RoutingNumber = e.Data.RoutingNumber
        }
      | RegisteredInternationalTransferRecipient e -> {
         FirstName = e.Data.FirstName
         LastName = e.Data.LastName
         AccountEnvironment = e.Data.AccountEnvironment
         Identification = e.Data.Identification
         IdentificationStrategy = e.Data.IdentificationStrategy
         Currency = e.Data.Currency
         RoutingNumber = None
        }

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

type TransferDeposited = {
   DepositedAmount: decimal
   Origin: string
}

module TransferDepositedEvent =
   let create (cmd: DepositTransferCommand) = {
      EntityId = cmd.EntityId
      Timestamp = cmd.Timestamp
      Data = {
         DepositedAmount = cmd.Amount
         Origin = cmd.Origin
      }
      CorrelationId = cmd.CorrelationId
   }

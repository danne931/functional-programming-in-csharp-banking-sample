namespace Bank.Transfer.Domain

open System

open Lib.SharedTypes

type TransferPending = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   Reference: string option
   Status: TransferProgress
}

type TransferProgressUpdate = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   Status: TransferProgress
}

type TransferApproved = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
}

type TransferRejected = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   Reason: TransferDeclinedReason
}

type RegisteredInternalTransferRecipient = {
   LastName: string
   FirstName: string
   AccountNumber: string
} with

   member x.toRecipient() = {
      FirstName = x.FirstName
      LastName = x.LastName
      AccountEnvironment = RecipientAccountEnvironment.Internal
      Identification = x.AccountNumber
      IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
      RoutingNumber = None
      Status = RecipientRegistrationStatus.Confirmed
   }

type RegisteredDomesticTransferRecipient = {
   LastName: string
   FirstName: string
   RoutingNumber: string option
   AccountNumber: string
} with

   member x.toRecipient() = {
      FirstName = x.FirstName
      LastName = x.LastName
      AccountEnvironment = RecipientAccountEnvironment.Domestic
      Identification = x.AccountNumber
      IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
      RoutingNumber = x.RoutingNumber
      // Domestic recipients are assumed valid until a failed transfer
      // request (with status AccountClosed/InvalidAccountInfo) proves otherwise.
      Status = RecipientRegistrationStatus.Confirmed
   }

type InternalRecipientDeactivated = {
   RecipientId: Guid
   RecipientName: string
//Reason: RecipientDeactivatedReason
}

type InternalSenderRegistered = {
   TransferSender: InternalTransferSender
}

type TransferDeposited = {
   DepositedAmount: decimal
   Origin: string
}

module TransferEventToTransaction =
   let fromPending (evt: BankEvent<TransferPending>) = {
      SenderAccountId = evt.EntityId
      TransactionId = evt.CorrelationId
      Recipient = evt.Data.Recipient
      Amount = evt.Data.DebitedAmount
      Date = evt.Data.Date
      Status = evt.Data.Status
   }

   let fromProgressUpdate (evt: BankEvent<TransferProgressUpdate>) = {
      SenderAccountId = evt.EntityId
      TransactionId = evt.CorrelationId
      Recipient = evt.Data.Recipient
      Amount = evt.Data.DebitedAmount
      Date = evt.Data.Date
      Status = evt.Data.Status
   }

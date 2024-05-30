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
      Nickname = None
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
      Nickname = None
      AccountEnvironment = RecipientAccountEnvironment.Domestic
      Identification = x.AccountNumber
      IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
      RoutingNumber = x.RoutingNumber
      // Domestic recipients are assumed valid until a failed transfer
      // request (with status AccountClosed/InvalidAccountInfo) proves otherwise.
      Status = RecipientRegistrationStatus.Confirmed
   }

// TODO: change RecipientId to VirtualId & add a VirtualId to TransferRecipient type
type InternalRecipientDeactivated = {
   RecipientId: AccountId
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

// TODO: change RecipientLookuKey to VirtualId & add a VirtualId to TransferRecipient type
type RecipientNicknamed = {
   RecipientLookupKey: string
   Nickname: string option
}

module TransferEventToTransaction =
   let fromPending (evt: BankEvent<TransferPending>) = {
      SenderAccountId = AccountId.fromEntityId evt.EntityId
      SenderOrgId = evt.OrgId
      TransferId = evt.CorrelationId
      Recipient = evt.Data.Recipient
      Amount = evt.Data.DebitedAmount
      Date = evt.Data.Date
      Status = evt.Data.Status
   }

   let fromProgressUpdate (evt: BankEvent<TransferProgressUpdate>) = {
      SenderAccountId = AccountId.fromEntityId evt.EntityId
      SenderOrgId = evt.OrgId
      TransferId = evt.CorrelationId
      Recipient = evt.Data.Recipient
      Amount = evt.Data.DebitedAmount
      Date = evt.Data.Date
      Status = evt.Data.Status
   }

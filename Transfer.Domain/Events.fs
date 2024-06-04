namespace Bank.Transfer.Domain

open System

open Lib.SharedTypes

type InternalTransferPending = {
   RecipientId: AccountId
   Amount: decimal
   Reference: string option
   TransferRequestDate: DateTime
}

type InternalTransferApproved = {
   RecipientId: AccountId
   Amount: decimal
   TransferRequestDate: DateTime
}

type InternalTransferRejected = {
   RecipientId: AccountId
   Amount: decimal
   Reason: TransferDeclinedReason
   TransferRequestDate: DateTime
}

type DomesticTransferPending = {
   Recipient: DomesticTransferRecipient
   TransferRequestDate: DateTime
   Amount: decimal
   Reference: string option
   Status: DomesticTransferProgress
}

type DomesticTransferProgressUpdate = {
   Recipient: DomesticTransferRecipient
   Amount: decimal
   Status: DomesticTransferProgress
   TransferRequestDate: DateTime
}

type DomesticTransferApproved = {
   Recipient: DomesticTransferRecipient
   Amount: decimal
   Status: DomesticTransferProgress
   TransferRequestDate: DateTime
}

type DomesticTransferRejected = {
   Recipient: DomesticTransferRecipient
   Amount: decimal
   Reason: TransferDeclinedReason
   TransferRequestDate: DateTime
}

type RegisteredInternalTransferRecipient = {
   Recipient: InternalTransferRecipient
}

type RegisteredDomesticTransferRecipient = {
   Recipient: DomesticTransferRecipient
}

type InternalRecipientDeactivated = {
   RecipientId: AccountId
   RecipientName: string
//Reason: RecipientDeactivatedReason
}

type InternalSenderRegistered = { Sender: InternalTransferSender }

type TransferDeposited = { Amount: decimal; Origin: AccountId }

type RecipientNicknamed = {
   RecipientId: AccountId
   RecipientAccountEnvironment: RecipientAccountEnvironment
   Nickname: string option
}

module TransferEventToDomesticTransfer =
   let fromPending
      (evt: BankEvent<DomesticTransferPending>)
      : DomesticTransfer
      =
      {
         SenderAccountId = AccountId.fromEntityId evt.EntityId
         SenderOrgId = evt.OrgId
         TransferId = evt.CorrelationId
         Recipient = evt.Data.Recipient
         Amount = evt.Data.Amount
         Date = evt.Data.TransferRequestDate
         Status = evt.Data.Status
      }

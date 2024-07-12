namespace Bank.Transfer.Domain

open System

open Lib.SharedTypes

type InternalTransferPending = {
   RecipientId: AccountId
   Amount: decimal
   Memo: string option
   ScheduledDate: DateTime
}

type InternalTransferApproved = {
   RecipientId: AccountId
   Amount: decimal
   ScheduledDate: DateTime
}

type InternalTransferRejected = {
   RecipientId: AccountId
   Amount: decimal
   Reason: TransferDeclinedReason
   ScheduledDate: DateTime
}

// Info received from the initial domestic transfer request will
// carry over unaltered for all further event progressions
// (ProgressUpdate/Approved/Rejected/Retry).
type BaseDomesticTransferInfo = {
   Sender: DomesticTransferSender
   Recipient: DomesticTransferRecipient
   ScheduledDate: DateTime
   Amount: decimal
   Memo: string option
}

type DomesticTransferPending = {
   BaseInfo: BaseDomesticTransferInfo
   Status: DomesticTransferProgress
}

type DomesticTransferProgressUpdate = {
   BaseInfo: BaseDomesticTransferInfo
   Status: DomesticTransferProgress
}

type DomesticTransferApproved = {
   BaseInfo: BaseDomesticTransferInfo
   Status: DomesticTransferProgress
}

type DomesticTransferRejected = {
   BaseInfo: BaseDomesticTransferInfo
   Reason: TransferDeclinedReason
}

type RegisteredInternalTransferRecipient = {
   Recipient: InternalTransferRecipient
}

type RegisteredDomesticTransferRecipient = {
   Recipient: DomesticTransferRecipient
}

type EditedDomesticTransferRecipient = { Recipient: DomesticTransferRecipient }

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
      let info = evt.Data.BaseInfo

      {
         Sender = info.Sender
         TransferId = evt.CorrelationId
         Recipient = info.Recipient
         InitiatedBy = evt.InitiatedById
         Amount = info.Amount
         ScheduledDate = info.ScheduledDate
         Memo = info.Memo
         Status = evt.Data.Status
      }

   let fromRejection
      (evt: BankEvent<DomesticTransferRejected>)
      : DomesticTransfer
      =
      let info = evt.Data.BaseInfo

      {
         Sender = info.Sender
         TransferId = evt.CorrelationId
         Recipient = info.Recipient
         InitiatedBy = evt.InitiatedById
         Amount = info.Amount
         ScheduledDate = info.ScheduledDate
         Memo = info.Memo
         Status = DomesticTransferProgress.Failed evt.Data.Reason
      }

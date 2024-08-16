namespace Bank.Transfer.Domain

open System

open Lib.SharedTypes

type InternalTransferWithinOrgPending = { BaseInfo: BaseInternalTransferInfo }

type InternalTransferWithinOrgApproved = { BaseInfo: BaseInternalTransferInfo }

type InternalTransferWithinOrgRejected = {
   BaseInfo: BaseInternalTransferInfo
   Reason: TransferDeclinedReason
}

type InternalTransferBetweenOrgsPending = {
   BaseInfo: BaseInternalTransferInfo
   Memo: string option
}

type InternalTransferBetweenOrgsApproved = {
   BaseInfo: BaseInternalTransferInfo
}

type InternalTransferBetweenOrgsRejected = {
   BaseInfo: BaseInternalTransferInfo
   Reason: TransferDeclinedReason
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

type RegisteredDomesticTransferRecipient = {
   Recipient: DomesticTransferRecipient
}

type EditedDomesticTransferRecipient = { Recipient: DomesticTransferRecipient }

type InternalTransferWithinOrgDeposited = {
   Amount: decimal
   Source: InternalTransferSender
}

type InternalTransferBetweenOrgsDeposited = {
   Amount: decimal
   Source: InternalTransferSender
}

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

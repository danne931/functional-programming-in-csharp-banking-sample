namespace Bank.Transfer.Domain

open System

open Lib.SharedTypes

type InternalTransferWithinOrgPending = { BaseInfo: BaseInternalTransferInfo }

type InternalTransferWithinOrgApproved = { BaseInfo: BaseInternalTransferInfo }

type InternalTransferWithinOrgRejected = {
   BaseInfo: BaseInternalTransferInfo
   Reason: InternalTransferDeclinedReason
}

type InternalTransferBetweenOrgsScheduled = {
   BaseInfo: BaseInternalTransferInfo
   Memo: string option
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
   Reason: InternalTransferDeclinedReason
}

type DomesticTransferScheduled = { BaseInfo: BaseDomesticTransferInfo }

type DomesticTransferPending = { BaseInfo: BaseDomesticTransferInfo }

type DomesticTransferProgressUpdate = {
   BaseInfo: BaseDomesticTransferInfo
   InProgressInfo: string
}

type DomesticTransferApproved = { BaseInfo: BaseDomesticTransferInfo }

type DomesticTransferRejected = {
   BaseInfo: BaseDomesticTransferInfo
   Reason: DomesticTransferDeclinedReason
}

type RegisteredDomesticTransferRecipient = {
   Recipient: DomesticTransferRecipient
}

type EditedDomesticTransferRecipient = { Recipient: DomesticTransferRecipient }

type InternalTransferWithinOrgDeposited = { BaseInfo: BaseInternalTransferInfo }

type InternalTransferBetweenOrgsDeposited = {
   BaseInfo: BaseInternalTransferInfo
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
         TransferId = evt.Data.BaseInfo.TransferId
         Recipient = info.Recipient
         InitiatedBy = evt.InitiatedById
         Amount = info.Amount
         ScheduledDate = info.ScheduledDate
         Memo = info.Memo
         Status = DomesticTransferProgress.Outgoing
      }

   let fromRejection
      (evt: BankEvent<DomesticTransferRejected>)
      : DomesticTransfer
      =
      let info = evt.Data.BaseInfo

      {
         Sender = info.Sender
         TransferId = evt.Data.BaseInfo.TransferId
         Recipient = info.Recipient
         InitiatedBy = evt.InitiatedById
         Amount = info.Amount
         ScheduledDate = info.ScheduledDate
         Memo = info.Memo
         Status = DomesticTransferProgress.Failed evt.Data.Reason
      }

type PlatformPaymentBaseInfo = {
   Id: PaymentId
   Payee: Payee
   Payer: PlatformPayer
   InitiatedById: InitiatedById
   Amount: decimal
}

type PlatformPaymentRequested = {
   BaseInfo: PlatformPaymentBaseInfo
   Expiration: DateTime
   Memo: string
}

module PlatformPaymentRequested =
   let fromPayment (p: PlatformPayment) : PlatformPaymentRequested = {
      BaseInfo = {
         Id = p.BaseInfo.Id
         Payee = p.BaseInfo.Payee
         Payer = p.Payer
         InitiatedById = p.BaseInfo.InitiatedBy
         Amount = p.BaseInfo.Amount
      }
      Memo = p.BaseInfo.Memo
      Expiration = p.BaseInfo.Expiration
   }

   let toPayment (e: BankEvent<PlatformPaymentRequested>) : PlatformPayment =
      let info = e.Data.BaseInfo

      {
         BaseInfo = {
            Id = info.Id
            InitiatedBy = info.InitiatedById
            Amount = info.Amount
            Type = PaymentType.Platform
            Payee = info.Payee
            CreatedAt = e.Timestamp
            Expiration = e.Data.Expiration
            Memo = e.Data.Memo
         }
         Status = PlatformPaymentStatus.Unpaid
         Payer = e.Data.BaseInfo.Payer
         PaidBy = None
      }

type PlatformPaymentPaid = {
   BaseInfo: PlatformPaymentBaseInfo
   PaymentMethod: PaymentMethod
}

type PlatformPaymentDeposited = {
   BaseInfo: PlatformPaymentBaseInfo
   PaymentMethod: PaymentMethod
}

type PlatformPaymentCancelled = {
   BaseInfo: PlatformPaymentBaseInfo
   Reason: string option
}

type PlatformPaymentDeclined = {
   BaseInfo: PlatformPaymentBaseInfo
   Reason: string option
}

type ThirdPartyPaymentBaseInfo = {
   Id: PaymentId
   Payee: Payee
   Payer: ThirdPartyPayer
   InitiatedById: InitiatedById
}

type ThirdPartyPaymentRequested = {
   BaseInfo: ThirdPartyPaymentBaseInfo
   Amount: decimal
   Expiration: DateTime
   Memo: string
}

type ThirdPartyPaymentDeposited = {
   BaseInfo: ThirdPartyPaymentBaseInfo
   PaymentMethod: ThirdPartyPaymentMethod
}

type ThirdPartyPaymentCancelled = {
   BaseInfo: ThirdPartyPaymentBaseInfo
   Reason: string option
}

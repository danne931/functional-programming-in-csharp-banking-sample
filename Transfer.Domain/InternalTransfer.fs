namespace Bank.Transfer.Domain

open System

open Lib.SharedTypes

type InternalTransferSender = {
   Name: string
   AccountId: AccountId
   OrgId: OrgId
}

[<RequireQualifiedAccess>]
type InternalTransferDeclinedReason =
   | AccountClosed
   | InvalidAccountInfo

type BaseInternalTransferInfo = {
   RecipientOrgId: OrgId
   RecipientId: AccountId
   RecipientName: string
   Amount: decimal
   ScheduledDate: DateTime
   Sender: InternalTransferSender
}

type InProgressInternalTransfer = {
   CorrelationId: CorrelationId
   Info: BaseInternalTransferInfo
}

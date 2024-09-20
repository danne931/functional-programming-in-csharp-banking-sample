namespace Bank.Transfer.Domain

open System

open Lib.SharedTypes

type InternalTransferRecipient = {
   Name: string
   AccountId: AccountId
   OrgId: OrgId
}

type InternalTransferSender = {
   Name: string
   AccountId: AccountId
   OrgId: OrgId
}

[<RequireQualifiedAccess>]
type InternalTransferDeclinedReason =
   | AccountClosed
   | InvalidAccountInfo

[<RequireQualifiedAccess>]
type InternalTransferStatus =
   | Scheduled
   | Pending
   | Approved
   | Deposited
   | Failed of InternalTransferDeclinedReason

type BaseInternalTransferInfo = {
   TransferId: TransferId
   InitiatedBy: InitiatedById
   Recipient: InternalTransferRecipient
   Amount: decimal
   ScheduledDate: DateTime
   Sender: InternalTransferSender
}

type InProgressInternalTransfer = {
   TransferId: TransferId
   Info: BaseInternalTransferInfo
}

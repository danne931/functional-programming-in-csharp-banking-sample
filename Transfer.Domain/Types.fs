namespace Bank.Transfer.Domain

open System

open Lib.SharedTypes

[<RequireQualifiedAccess>]
type RecipientAccountEnvironment =
   | Internal
   | Domestic

[<RequireQualifiedAccess>]
type RecipientRegistrationStatus =
   | Confirmed
   | InvalidAccount
   | Closed

type InternalTransferRecipient = {
   Name: string
   Nickname: string option
   AccountId: AccountId
   Status: RecipientRegistrationStatus
}

type InternalTransferSender = {
   Name: string
   AccountId: AccountId
   OrgId: OrgId
}

[<RequireQualifiedAccess>]
type DomesticRecipientAccountDepository =
   | Checking
   | Savings

[<RequireQualifiedAccess>]
type PaymentNetwork = | ACH
//| FedNow

type DomesticTransferRecipient = {
   LastName: string
   FirstName: string
   Nickname: string option
   AccountNumber: AccountNumber
   RoutingNumber: RoutingNumber
   Status: RecipientRegistrationStatus
   AccountId: AccountId
   Depository: DomesticRecipientAccountDepository
   PaymentNetwork: PaymentNetwork
} with

   member x.Name = $"{x.FirstName} {x.LastName}"

[<RequireQualifiedAccess>]
type TransferRecipient =
   | Internal of InternalTransferRecipient
   | Domestic of DomesticTransferRecipient

[<RequireQualifiedAccess>]
type TransferProgressTrackingMessage = | ProgressCheck

[<RequireQualifiedAccess>]
type TransferDeclinedReason =
   | CorruptData
   | InvalidAction
   | InvalidPaymentNetwork
   | InvalidDepository
   | InvalidAmount
   | AccountClosed
   | InvalidAccountInfo
   | Unknown of string

[<RequireQualifiedAccess>]
type DomesticTransferProgress =
   | Outgoing
   | InProgress of string
   | Complete
   | Failed of TransferDeclinedReason

type DomesticTransferSender = {
   Name: string
   AccountNumber: AccountNumber
   RoutingNumber: RoutingNumber
   OrgId: OrgId
   AccountId: AccountId
}

type DomesticTransfer = {
   Sender: DomesticTransferSender
   Recipient: DomesticTransferRecipient
   TransferId: CorrelationId
   InitiatedBy: InitiatedById
   Amount: decimal
   ScheduledDate: DateTime
   Status: DomesticTransferProgress
   Memo: string option
}

namespace Bank.Transfer.Domain

open System
open System.Threading.Tasks

open Lib.SharedTypes

[<RequireQualifiedAccess>]
type RecipientRegistrationStatus =
   | Confirmed
   | InvalidAccount
   | Closed

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
   CreatedAt: DateTime
} with

   member x.Name = $"{x.FirstName} {x.LastName}"

[<RequireQualifiedAccess>]
type TransferProgressTrackingMessage = | ProgressCheck

[<RequireQualifiedAccess>]
type DomesticTransferDeclinedReason =
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
   | Failed of DomesticTransferDeclinedReason

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

[<RequireQualifiedAccess>]
type DomesticTransferServiceAction =
   | TransferRequest
   | ProgressCheck

type DomesticTransferServiceSender = {
   Name: string
   AccountNumber: string
   RoutingNumber: string
}

type DomesticTransferServiceRecipient = {
   Name: string
   AccountNumber: string
   RoutingNumber: string
   Depository: string
}

type DomesticTransferServiceResponse = {
   Sender: DomesticTransferServiceSender
   Recipient: DomesticTransferServiceRecipient
   Ok: bool
   Status: string
   Reason: string
   TransactionId: string
}

type DomesticTransferRequest =
   DomesticTransferServiceAction
      -> DomesticTransfer
      -> Task<Result<DomesticTransferServiceResponse, Err>>

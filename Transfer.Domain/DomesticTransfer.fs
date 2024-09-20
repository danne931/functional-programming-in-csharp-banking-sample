namespace Bank.Transfer.Domain

open System
open System.Threading.Tasks

open Lib.SharedTypes

[<RequireQualifiedAccess>]
type RecipientRegistrationStatus =
   | Confirmed
   | InvalidAccount
   | Closed

module RecipientRegistrationStatus =
   let fromString (str: string) : RecipientRegistrationStatus option =
      match str.ToLower() with
      | "confirmed" -> Some RecipientRegistrationStatus.Confirmed
      | "invalidaccount" -> Some RecipientRegistrationStatus.InvalidAccount
      | "closed" -> Some RecipientRegistrationStatus.Closed
      | _ -> None

   let fromStringUnsafe str : RecipientRegistrationStatus =
      match fromString str with
      | Some s -> s
      | None ->
         failwith
            "Error attempting to cast string to RecipientRegistrationStatus"

[<RequireQualifiedAccess>]
type DomesticRecipientAccountDepository =
   | Checking
   | Savings

module DomesticRecipientAccountDepository =
   let fromString (str: string) : DomesticRecipientAccountDepository option =
      match str.ToLower() with
      | "checking" -> Some DomesticRecipientAccountDepository.Checking
      | "savings" -> Some DomesticRecipientAccountDepository.Savings
      | _ -> None

   let fromStringUnsafe str : DomesticRecipientAccountDepository =
      match fromString str with
      | Some s -> s
      | None ->
         failwith
            "Error attempting to cast string to DomesticRecipientAccountDepository"

[<RequireQualifiedAccess>]
type PaymentNetwork = | ACH
//| FedNow

module PaymentNetwork =
   let fromString (str: string) : PaymentNetwork option =
      match str.ToLower() with
      | "ach" -> Some PaymentNetwork.ACH
      | _ -> None

   let fromStringUnsafe str : PaymentNetwork =
      match fromString str with
      | Some s -> s
      | None -> failwith "Error attempting to cast string to PaymentNetwork"

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
   | Scheduled
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

// Info received from the initial domestic transfer request will
// carry over unaltered for all event progressions
// (ProgressUpdate/Approved/Rejected/Retry).
type BaseDomesticTransferInfo = {
   Sender: DomesticTransferSender
   Recipient: DomesticTransferRecipient
   Amount: decimal
   TransferId: TransferId
   InitiatedBy: InitiatedById
   Memo: string option
   ScheduledDate: DateTime
}

type DomesticTransfer = {
   Sender: DomesticTransferSender
   Recipient: DomesticTransferRecipient
   TransferId: TransferId
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

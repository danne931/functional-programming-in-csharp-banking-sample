namespace Bank.Transfer.Domain

open System
open System.Threading.Tasks

open Lib.SharedTypes

[<RequireQualifiedAccess>]
type TransferProgressTrackingMessage = | ProgressCheck

[<RequireQualifiedAccess>]
type TransferProgress =
   | Outgoing
   | InProgress of string
   | Complete

[<RequireQualifiedAccess>]
type RecipientAccountEnvironment =
   | Internal
   | Domestic

[<RequireQualifiedAccess>]
type RecipientAccountIdentificationStrategy =
   | AccountId
   | SwiftBIC
   | IBAN
   | NationalID

[<RequireQualifiedAccess>]
type RecipientRegistrationStatus =
   | Confirmed
   | InvalidAccount
   | Closed

type TransferRecipient = {
   LastName: string
   FirstName: string
   Identification: string
   AccountEnvironment: RecipientAccountEnvironment
   IdentificationStrategy: RecipientAccountIdentificationStrategy
   RoutingNumber: string option
   Status: RecipientRegistrationStatus
} with

   member x.Name = $"{x.FirstName} {x.LastName}"

type InternalTransferSender = { Name: string; AccountId: Guid }

type TransferServiceResponse = {
   AccountNumber: string
   RoutingNumber: string option
   Ok: bool
   Status: string
   Reason: string
   TransactionId: string
}

type TransferTransaction = {
   SenderAccountId: Guid
   TransactionId: Guid
   Recipient: TransferRecipient
   Amount: decimal
   Date: DateTime
   Status: TransferProgress
}

[<RequireQualifiedAccess>]
type TransferServiceAction =
   | TransferRequest
   | ProgressCheck

[<RequireQualifiedAccess>]
type InternalTransferMessage =
   | TransferRequest of TransferTransaction
   | ConfirmRecipient of InternalTransferSender * TransferRecipient

[<RequireQualifiedAccess>]
type DomesticTransferMessage =
   | TransferRequest of TransferServiceAction * TransferTransaction
   | TransferResponse of
      TransferServiceResponse *
      TransferServiceAction *
      TransferTransaction
   | BreakerHalfOpen
   | BreakerClosed

[<RequireQualifiedAccess>]
type TransferDeclinedReason =
   | CorruptData
   | InvalidAction
   | InvalidAmount
   | AccountClosed
   | InvalidAccountInfo
   | Unknown of string

type TransferRequest =
   TransferServiceAction
      -> TransferTransaction
      -> Task<Result<TransferServiceResponse, Err>>

type GetInProgressTransfers =
   unit -> Result<Option<TransferTransaction list>, Err> Async

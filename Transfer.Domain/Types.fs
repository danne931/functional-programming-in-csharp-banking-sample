namespace Bank.Transfer.Domain

open System
open System.Threading.Tasks

open Lib.SharedTypes

type TransferProgressTrackingMessage = | ProgressCheck

type TransferProgress =
   | Outgoing
   | InProgress of string
   | Complete

type RecipientAccountEnvironment =
   | Internal
   | Domestic

type RecipientAccountIdentificationStrategy =
   | AccountId
   | SwiftBIC
   | IBAN
   | NationalID

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

type TransferServiceAction =
   | TransferRequest
   | ProgressCheck

type InternalTransferMessage =
   | TransferRequest of TransferTransaction
   | ConfirmRecipient of InternalTransferSender * TransferRecipient

type DomesticTransferMessage =
   | TransferRequest of TransferServiceAction * TransferTransaction
   | TransferResponse of
      TransferServiceResponse *
      TransferServiceAction *
      TransferTransaction
   | BreakerHalfOpen
   | BreakerClosed

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

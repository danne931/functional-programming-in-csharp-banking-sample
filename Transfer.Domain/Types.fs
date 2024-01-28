namespace Bank.Transfer.Domain

open System
open FsToolkit.ErrorHandling

open Lib.Types

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

type TransferRecipient = {
   LastName: string
   FirstName: string
   Identification: string
   AccountEnvironment: RecipientAccountEnvironment
   IdentificationStrategy: RecipientAccountIdentificationStrategy
   RoutingNumber: string option
   Currency: Currency
}

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

type DomesticTransferMessage =
   | TransferRequest of TransferServiceAction * TransferTransaction
   | TransferResponse of
      TransferServiceResponse *
      TransferServiceAction *
      TransferTransaction
   | BreakerHalfOpen
   | BreakerClosed

type TransferRequest =
   TransferServiceAction
      -> TransferTransaction
      -> TaskResult<TransferServiceResponse, string>

type GetInProgressTransfers =
   unit -> Result<Option<TransferTransaction list>, Err> Async

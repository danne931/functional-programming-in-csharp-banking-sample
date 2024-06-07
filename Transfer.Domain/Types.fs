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
   LastName: string
   FirstName: string
   Nickname: string option
   AccountId: AccountId
   Status: RecipientRegistrationStatus
} with

   member x.Name = $"{x.FirstName} {x.LastName}"

type InternalTransferSender = {
   Name: string
   AccountId: AccountId
   OrgId: OrgId
}

type DomesticTransferRecipient = {
   LastName: string
   FirstName: string
   Nickname: string option
   // TODO: Make types for account/routing number with validus validation
   AccountNumber: string
   RoutingNumber: string
   Status: RecipientRegistrationStatus
   AccountId: AccountId
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
   | InvalidAmount
   | AccountClosed
   | InvalidAccountInfo
   | Unknown of string

[<RequireQualifiedAccess>]
type DomesticTransferProgress =
   | Outgoing
   | InProgress of string
   | Complete

type DomesticTransfer = {
   SenderOrgId: OrgId
   SenderAccountId: AccountId
   TransferId: CorrelationId
   Recipient: DomesticTransferRecipient
   Amount: decimal
   Date: DateTime
   Status: DomesticTransferProgress
}

[<RequireQualifiedAccess>]
type DomesticTransferServiceAction =
   | TransferRequest
   | ProgressCheck

type DomesticTransferServiceResponse = {
   AccountNumber: string
   RoutingNumber: string
   Ok: bool
   Status: string
   Reason: string
   TransactionId: string
}

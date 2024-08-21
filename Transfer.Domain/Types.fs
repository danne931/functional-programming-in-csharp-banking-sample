namespace Bank.Transfer.Domain

open System

open Lib.SharedTypes

[<RequireQualifiedAccess>]
type RecipientAccountEnvironment =
   | InternalWithinOrg
   | InternalBetweenOrgs
   | Domestic

module RecipientAccountEnvironment =
   let fromString (str: string) : RecipientAccountEnvironment option =
      match str.ToLower() with
      | "internalwithinorg" ->
         Some RecipientAccountEnvironment.InternalWithinOrg
      | "internalbetweenorgs" ->
         Some RecipientAccountEnvironment.InternalBetweenOrgs
      | "domestic" -> Some RecipientAccountEnvironment.Domestic
      | _ -> None

   let fromStringUnsafe str : RecipientAccountEnvironment =
      match fromString str with
      | Some s -> s
      | None ->
         failwith
            "Error attempting to cast string to RecipientAccountEnvironment"

[<RequireQualifiedAccess>]
type RecipientRegistrationStatus =
   | Confirmed
   | InvalidAccount
   | Closed

type InternalTransferRecipient = {
   Name: string
   Nickname: string option
   AccountId: AccountId
   OrgId: OrgId
}

type InternalTransferSender = {
   Name: string
   AccountId: AccountId
   OrgId: OrgId
}

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

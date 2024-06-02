namespace Bank.Transfer.Domain

open System
open Validus

open Lib.SharedTypes
open Lib.Validators

type TransferInput = {
   Recipient: TransferRecipient
   Date: DateTime
   Amount: decimal
   Reference: string option
}

type TransferCommand = Command<TransferInput>

module TransferCommand =
   let create (accountId: AccountId, orgId: OrgId) (data: TransferInput) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: TransferCommand)
      : ValidationResult<BankEvent<TransferPending>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Transfer amount" input.Amount
         let! _ = dateNotDefaultValidator "Transfer date" input.Date

         return
            BankEvent.create<TransferInput, TransferPending> cmd {
               Recipient = input.Recipient
               Date = input.Date
               DebitedAmount = input.Amount
               Reference = input.Reference
               Status = TransferProgress.Outgoing
            }
      }

type TransferProgressInput = {
   Recipient: TransferRecipient
   Date: DateTime
   Amount: decimal
   Status: TransferProgress
}

type UpdateTransferProgressCommand = Command<TransferProgressInput>

module UpdateTransferProgressCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: TransferProgressInput)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: UpdateTransferProgressCommand)
      : ValidationResult<BankEvent<TransferProgressUpdate>>
      =
      let input = cmd.Data

      BankEvent.create<TransferProgressInput, TransferProgressUpdate> cmd {
         Recipient = input.Recipient
         Date = input.Date
         DebitedAmount = input.Amount
         Status = input.Status
      }
      |> Ok

type ApproveTransferInput = {
   Recipient: TransferRecipient
   Date: DateTime
   Amount: decimal
}

type ApproveTransferCommand = Command<ApproveTransferInput>

module ApproveTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: ApproveTransferInput)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: ApproveTransferCommand)
      : ValidationResult<BankEvent<TransferApproved>>
      =
      let input = cmd.Data

      BankEvent.create<ApproveTransferInput, TransferApproved> cmd {
         Recipient = input.Recipient
         Date = input.Date
         DebitedAmount = input.Amount
      }
      |> Ok

type RejectTransferInput = {
   Recipient: TransferRecipient
   Date: DateTime
   Amount: decimal
   Reason: TransferDeclinedReason
}

type RejectTransferCommand = Command<RejectTransferInput>

module RejectTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: RejectTransferInput)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: RejectTransferCommand)
      : ValidationResult<BankEvent<TransferRejected>>
      =
      let input = cmd.Data
      // Updates status of transfer recipient when a transfer is declined
      // due to an account not existing or becoming closed.
      let updatedRecipientStatus =
         match input.Reason with
         | TransferDeclinedReason.InvalidAccountInfo ->
            RecipientRegistrationStatus.InvalidAccount
         | TransferDeclinedReason.AccountClosed ->
            RecipientRegistrationStatus.Closed
         | _ -> input.Recipient.Status

      BankEvent.create<RejectTransferInput, TransferRejected> cmd {
         Recipient = {
            input.Recipient with
               Status = updatedRecipientStatus
         }
         Date = input.Date
         DebitedAmount = input.Amount
         Reason = input.Reason
      }
      |> Ok

type DepositTransferInput = { Amount: decimal; Origin: AccountId }
type DepositTransferCommand = Command<DepositTransferInput>

module DepositTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: DepositTransferInput)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: DepositTransferCommand)
      : ValidationResult<BankEvent<TransferDeposited>>
      =
      BankEvent.create<DepositTransferInput, TransferDeposited> cmd {
         DepositedAmount = cmd.Data.Amount
         Origin = cmd.Data.Origin
      }
      |> Ok

type RegisterTransferRecipientInput = { Recipient: TransferRecipient }
type RegisterTransferRecipientCommand = Command<RegisterTransferRecipientInput>

module RegisterTransferRecipientCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: RegisterTransferRecipientInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

module TransferRecipientEvent =
   let recipientValidation (cmd: RegisterTransferRecipientCommand) = validate {
      let recipient = cmd.Data.Recipient

      let! _ =
         transferRecipientIdValidator
            (string cmd.EntityId)
            recipient.Identification

      and! _ = accountNumberValidator recipient.Identification

      and! _ = firstNameValidator recipient.FirstName
      and! _ = lastNameValidator recipient.LastName
      return cmd
   }

   let local
      (cmd: RegisterTransferRecipientCommand)
      : ValidationResult<BankEvent<RegisteredInternalTransferRecipient>>
      =
      validate {
         let! _ = recipientValidation cmd
         let recipient = cmd.Data.Recipient

         return
            BankEvent.create<
               RegisterTransferRecipientInput,
               RegisteredInternalTransferRecipient
             >
               cmd
               {
                  AccountNumber = recipient.Identification
                  LastName = recipient.LastName
                  FirstName = recipient.FirstName
               }
      }

   let domestic
      (cmd: RegisterTransferRecipientCommand)
      : ValidationResult<BankEvent<RegisteredDomesticTransferRecipient>>
      =
      validate {
         let! _ = recipientValidation cmd
         and! _ = routingNumberValidator cmd.Data.Recipient.RoutingNumber
         let recipient = cmd.Data.Recipient

         return
            BankEvent.create<
               RegisterTransferRecipientInput,
               RegisteredDomesticTransferRecipient
             >
               cmd
               {
                  LastName = recipient.LastName
                  FirstName = recipient.FirstName
                  AccountNumber = recipient.Identification
                  RoutingNumber = recipient.RoutingNumber
               }
      }

type RegisterInternalSenderInput = { Sender: InternalTransferSender }
type RegisterInternalSenderCommand = Command<RegisterInternalSenderInput>

module RegisterInternalSenderCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: RegisterInternalSenderInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: RegisterInternalSenderCommand)
      : ValidationResult<BankEvent<InternalSenderRegistered>>
      =
      BankEvent.create<RegisterInternalSenderInput, InternalSenderRegistered>
         cmd
         { TransferSender = cmd.Data.Sender }
      |> Ok

type DeactivateInternalRecipientInput = {
   RecipientId: AccountId
   RecipientName: string
}

type DeactivateInternalRecipientCommand =
   Command<DeactivateInternalRecipientInput>

module DeactivateInternalRecipientCommand =
   let create
      (sender: InternalTransferSender)
      (data: DeactivateInternalRecipientInput)
      =
      Command.create
         (AccountId.toEntityId sender.AccountId)
         sender.OrgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: DeactivateInternalRecipientCommand)
      : ValidationResult<BankEvent<InternalRecipientDeactivated>>
      =
      BankEvent.create<
         DeactivateInternalRecipientInput,
         InternalRecipientDeactivated
       >
         cmd
         {
            RecipientId = cmd.Data.RecipientId
            RecipientName = cmd.Data.RecipientName
         }
      |> Ok

type NicknameRecipientInput = {
   Recipient: TransferRecipient
   Nickname: string option
}

type NicknameRecipientCommand = Command<NicknameRecipientInput>

module NicknameRecipientCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: NicknameRecipientInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: NicknameRecipientCommand)
      : ValidationResult<BankEvent<RecipientNicknamed>>
      =
      BankEvent.create<NicknameRecipientInput, RecipientNicknamed> cmd {
         RecipientLookupKey = cmd.Data.Recipient.LookupKey
         Nickname = cmd.Data.Nickname
      }
      |> Ok

module TransferTransactionToCommand =
   let progress (txn: TransferTransaction) (status: TransferProgress) =
      UpdateTransferProgressCommand.create
         (txn.SenderAccountId, txn.SenderOrgId)
         txn.TransferId
         {
            Recipient = txn.Recipient
            Date = txn.Date
            Amount = txn.Amount
            Status = status
         }

   let approve (txn: TransferTransaction) =
      ApproveTransferCommand.create
         (txn.SenderAccountId, txn.SenderOrgId)
         txn.TransferId
         {
            Recipient = txn.Recipient
            Date = txn.Date
            Amount = txn.Amount
         }

   let reject (txn: TransferTransaction) (reason: TransferDeclinedReason) =
      RejectTransferCommand.create
         (txn.SenderAccountId, txn.SenderOrgId)
         txn.TransferId
         {
            Recipient = txn.Recipient
            Date = txn.Date
            Amount = txn.Amount
            Reason = reason
         }

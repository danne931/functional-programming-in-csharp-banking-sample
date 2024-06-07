namespace Bank.Transfer.Domain

open Validus
open System

open Lib.SharedTypes
open Lib.Validators

type InternalTransferCommand = Command<InternalTransferPending>

module InternalTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: InternalTransferPending)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: InternalTransferCommand)
      : ValidationResult<BankEvent<InternalTransferPending>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Transfer amount" input.Amount

         let! _ =
            dateNotDefaultValidator "Transfer date" input.TransferRequestDate

         return BankEvent.create<InternalTransferPending> cmd
      }

type ApproveInternalTransferCommand = Command<InternalTransferApproved>

module ApproveInternalTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: InternalTransferApproved)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: ApproveInternalTransferCommand)
      : ValidationResult<BankEvent<InternalTransferApproved>>
      =
      BankEvent.create<InternalTransferApproved> cmd |> Ok

type RejectInternalTransferCommand = Command<InternalTransferRejected>

module RejectInternalTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: InternalTransferRejected)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: RejectInternalTransferCommand)
      : ValidationResult<BankEvent<InternalTransferRejected>>
      =
      BankEvent.create<InternalTransferRejected> cmd |> Ok

type DepositTransferCommand = Command<TransferDeposited>

module DepositTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: TransferDeposited)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: DepositTransferCommand)
      : ValidationResult<BankEvent<TransferDeposited>>
      =
      BankEvent.create<TransferDeposited> cmd |> Ok

type InternalTransferRecipientInput = {
   LastName: string
   FirstName: string
   AccountId: AccountId
}

type RegisterInternalTransferRecipientCommand =
   Command<InternalTransferRecipientInput>

module RegisterInternalTransferRecipientCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: InternalTransferRecipientInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent (cmd: RegisterInternalTransferRecipientCommand) = validate {
      let! _ =
         transferRecipientIdValidator
            (string cmd.EntityId)
            (string cmd.Data.AccountId)

      and! _ = firstNameValidator cmd.Data.FirstName
      and! _ = lastNameValidator cmd.Data.LastName

      return
         BankEvent.create2<
            InternalTransferRecipientInput,
            RegisteredInternalTransferRecipient
          >
            cmd
            {
               Recipient = {
                  LastName = cmd.Data.LastName
                  FirstName = cmd.Data.FirstName
                  Nickname = None
                  AccountId = cmd.Data.AccountId
                  Status = RecipientRegistrationStatus.Confirmed
               }
            }
   }

type RegisterInternalSenderCommand = Command<InternalSenderRegistered>

module RegisterInternalSenderCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: InternalSenderRegistered)
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
      BankEvent.create<InternalSenderRegistered> cmd |> Ok

type DeactivateInternalRecipientCommand = Command<InternalRecipientDeactivated>

module DeactivateInternalRecipientCommand =
   let create
      (sender: InternalTransferSender)
      (data: InternalRecipientDeactivated)
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
      BankEvent.create<InternalRecipientDeactivated> cmd |> Ok

type NicknameRecipientCommand = Command<RecipientNicknamed>

module NicknameRecipientCommand =
   let create (accountId: AccountId, orgId: OrgId) (data: RecipientNicknamed) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: NicknameRecipientCommand)
      : ValidationResult<BankEvent<RecipientNicknamed>>
      =
      BankEvent.create<RecipientNicknamed> cmd |> Ok

type DomesticTransferRecipientInput = {
   LastName: string
   FirstName: string
   AccountNumber: string
   RoutingNumber: string
}

type RegisterDomesticTransferRecipientCommand =
   Command<DomesticTransferRecipientInput>

module RegisterDomesticTransferRecipientCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: DomesticTransferRecipientInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: RegisterDomesticTransferRecipientCommand)
      : ValidationResult<BankEvent<RegisteredDomesticTransferRecipient>>
      =
      validate {

         let recipient = {
            FirstName = cmd.Data.FirstName
            LastName = cmd.Data.LastName
            Nickname = None
            AccountNumber = cmd.Data.AccountNumber
            RoutingNumber = cmd.Data.RoutingNumber
            Status = RecipientRegistrationStatus.Confirmed
            AccountId = Guid.NewGuid() |> AccountId
         }

         let! _ =
            transferRecipientIdValidator
               (string cmd.EntityId)
               (string recipient.AccountId)

         and! _ = accountNumberValidator recipient.AccountNumber
         and! _ = routingNumberValidator recipient.RoutingNumber
         and! _ = firstNameValidator recipient.FirstName
         and! _ = lastNameValidator recipient.LastName

         return
            BankEvent.create2<
               DomesticTransferRecipientInput,
               RegisteredDomesticTransferRecipient
             >
               cmd
               { Recipient = recipient }
      }

type DomesticTransferPendingInput = {
   TransferRequestDate: DateTime
   Amount: Decimal
   Recipient: DomesticTransferRecipient
   Reference: string option
}

type DomesticTransferCommand = Command<DomesticTransferPendingInput>

module DomesticTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: DomesticTransferPendingInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         data

   let toEvent
      (cmd: DomesticTransferCommand)
      : ValidationResult<BankEvent<DomesticTransferPending>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Transfer amount" input.Amount

         let! _ =
            dateNotDefaultValidator "Transfer date" input.TransferRequestDate

         return
            BankEvent.create2<
               DomesticTransferPendingInput,
               DomesticTransferPending
             >
               cmd
               {
                  TransferRequestDate = cmd.Data.TransferRequestDate
                  Amount = cmd.Data.Amount
                  Recipient = cmd.Data.Recipient
                  Reference = cmd.Data.Reference
                  Status = DomesticTransferProgress.Outgoing
               }
      }

type ApproveDomesticTransferCommand = Command<DomesticTransferApproved>

module ApproveDomesticTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: DomesticTransferApproved)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: ApproveDomesticTransferCommand)
      : ValidationResult<BankEvent<DomesticTransferApproved>>
      =
      BankEvent.create<DomesticTransferApproved> cmd |> Ok

type RejectDomesticTransferCommand = Command<DomesticTransferRejected>

module RejectDomesticTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: DomesticTransferRejected)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: RejectDomesticTransferCommand)
      : ValidationResult<BankEvent<DomesticTransferRejected>>
      =
      BankEvent.create<DomesticTransferRejected> cmd |> Ok

type UpdateDomesticTransferProgressCommand =
   Command<DomesticTransferProgressUpdate>

module UpdateDomesticTransferProgressCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (data: DomesticTransferProgressUpdate)
      =
      Command.create (AccountId.toEntityId accountId) orgId correlationId data

   let toEvent
      (cmd: UpdateDomesticTransferProgressCommand)
      : ValidationResult<BankEvent<DomesticTransferProgressUpdate>>
      =
      BankEvent.create<DomesticTransferProgressUpdate> cmd |> Ok

module DomesticTransferToCommand =
   let progress (txn: DomesticTransfer) (status: DomesticTransferProgress) =
      UpdateDomesticTransferProgressCommand.create
         (txn.SenderAccountId, txn.SenderOrgId)
         txn.TransferId
         {
            Recipient = txn.Recipient
            TransferRequestDate = txn.Date
            Amount = txn.Amount
            Status = status
         }

   let approve (txn: DomesticTransfer) =
      ApproveDomesticTransferCommand.create
         (txn.SenderAccountId, txn.SenderOrgId)
         txn.TransferId
         {
            Recipient = txn.Recipient
            TransferRequestDate = txn.Date
            Amount = txn.Amount
            Status = txn.Status
         }

   let reject (txn: DomesticTransfer) (reason: TransferDeclinedReason) =
      RejectDomesticTransferCommand.create
         (txn.SenderAccountId, txn.SenderOrgId)
         txn.TransferId
         {
            Recipient = txn.Recipient
            TransferRequestDate = txn.Date
            Amount = txn.Amount
            Reason = reason
         }

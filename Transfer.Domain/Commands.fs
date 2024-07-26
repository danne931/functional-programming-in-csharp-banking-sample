namespace Bank.Transfer.Domain

open Validus
open System

open Lib.SharedTypes
open Lib.Validators

type InternalTransferCommand = Command<InternalTransferPending>

module InternalTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: InternalTransferPending)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent
      (cmd: InternalTransferCommand)
      : ValidationResult<BankEvent<InternalTransferPending>>
      =
      validate {
         let input = cmd.Data.BaseInfo
         let! _ = amountValidator "Transfer amount" input.Amount

         let! _ = dateNotDefaultValidator "Transfer date" input.ScheduledDate

         return BankEvent.create<InternalTransferPending> cmd
      }

type ApproveInternalTransferCommand = Command<InternalTransferApproved>

module ApproveInternalTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (initiatedBy: InitiatedById)
      (data: InternalTransferApproved)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

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
      (initiatedBy: InitiatedById)
      (data: InternalTransferRejected)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

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
      (initiatedBy: InitiatedById)
      (data: TransferDeposited)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

   let toEvent
      (cmd: DepositTransferCommand)
      : ValidationResult<BankEvent<TransferDeposited>>
      =
      BankEvent.create<TransferDeposited> cmd |> Ok

type InternalTransferRecipientInput = { Name: string; AccountId: AccountId }

type RegisterInternalTransferRecipientCommand =
   Command<InternalTransferRecipientInput>

module RegisterInternalTransferRecipientCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: InternalTransferRecipientInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent (cmd: RegisterInternalTransferRecipientCommand) = validate {
      let! _ =
         transferRecipientIdValidator
            (string cmd.EntityId)
            (string cmd.Data.AccountId)

      and! _ = accountNameValidator cmd.Data.Name

      return
         BankEvent.create2<
            InternalTransferRecipientInput,
            RegisteredInternalTransferRecipient
          >
            cmd
            {
               Recipient = {
                  Name = cmd.Data.Name
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
         (Guid.Empty |> EmployeeId |> InitiatedById)
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
      (initiatedBy: InitiatedById)
      (data: InternalRecipientDeactivated)
      =
      Command.create
         (AccountId.toEntityId sender.AccountId)
         sender.OrgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent
      (cmd: DeactivateInternalRecipientCommand)
      : ValidationResult<BankEvent<InternalRecipientDeactivated>>
      =
      BankEvent.create<InternalRecipientDeactivated> cmd |> Ok

type NicknameRecipientCommand = Command<RecipientNicknamed>

module NicknameRecipientCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: RecipientNicknamed)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         (initiatedBy: InitiatedById)
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
   Depository: DomesticRecipientAccountDepository
   PaymentNetwork: PaymentNetwork
}

type RegisterDomesticTransferRecipientCommand =
   Command<DomesticTransferRecipientInput>

module RegisterDomesticTransferRecipientCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: DomesticTransferRecipientInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent
      (cmd: RegisterDomesticTransferRecipientCommand)
      : ValidationResult<BankEvent<RegisteredDomesticTransferRecipient>>
      =
      validate {
         let! accountNumber =
            accountNumberValidator "Account Number" cmd.Data.AccountNumber

         and! routingNumber =
            routingNumberValidator "Routing Number" cmd.Data.RoutingNumber

         and! firstName = firstNameValidator cmd.Data.FirstName
         and! lastName = lastNameValidator cmd.Data.LastName

         let recipient = {
            FirstName = firstName
            LastName = lastName
            Nickname = None
            AccountNumber = accountNumber
            RoutingNumber = routingNumber
            Status = RecipientRegistrationStatus.Confirmed
            AccountId = Guid.NewGuid() |> AccountId
            Depository = cmd.Data.Depository
            PaymentNetwork = cmd.Data.PaymentNetwork
         }

         return
            BankEvent.create2<
               DomesticTransferRecipientInput,
               RegisteredDomesticTransferRecipient
             >
               cmd
               { Recipient = recipient }
      }

type EditDomesticTransferRecipientInput = {
   RecipientWithoutAppliedUpdates: DomesticTransferRecipient
   LastName: string
   FirstName: string
   AccountNumber: string
   RoutingNumber: string
   Depository: DomesticRecipientAccountDepository
   PaymentNetwork: PaymentNetwork
}

type EditDomesticTransferRecipientCommand =
   Command<EditDomesticTransferRecipientInput>

module EditDomesticTransferRecipientCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: EditDomesticTransferRecipientInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent
      (cmd: EditDomesticTransferRecipientCommand)
      : ValidationResult<BankEvent<EditedDomesticTransferRecipient>>
      =
      validate {
         let! accountNumber =
            accountNumberValidator "Account Number" cmd.Data.AccountNumber

         and! routingNumber =
            routingNumberValidator "Routing Number" cmd.Data.RoutingNumber

         and! firstName = firstNameValidator cmd.Data.FirstName
         and! lastName = lastNameValidator cmd.Data.LastName

         let recipient = {
            cmd.Data.RecipientWithoutAppliedUpdates with
               FirstName = firstName
               LastName = lastName
               AccountNumber = accountNumber
               RoutingNumber = routingNumber
               Depository = cmd.Data.Depository
               PaymentNetwork = cmd.Data.PaymentNetwork
         }

         return
            BankEvent.create2<
               EditDomesticTransferRecipientInput,
               EditedDomesticTransferRecipient
             >
               cmd
               { Recipient = recipient }
      }

type DomesticTransferPendingInput = {
   ScheduledDate: DateTime
   Amount: Decimal
   Sender: DomesticTransferSender
   Recipient: DomesticTransferRecipient
   Memo: string option
}

type DomesticTransferCommand = Command<DomesticTransferPendingInput>

module DomesticTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: DomesticTransferPendingInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent
      (cmd: DomesticTransferCommand)
      : ValidationResult<BankEvent<DomesticTransferPending>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Transfer amount" input.Amount

         let! _ = dateNotDefaultValidator "Transfer date" input.ScheduledDate

         return
            BankEvent.create2<
               DomesticTransferPendingInput,
               DomesticTransferPending
             >
               cmd
               {
                  BaseInfo = {
                     ScheduledDate = cmd.Data.ScheduledDate
                     Amount = cmd.Data.Amount
                     Sender = cmd.Data.Sender
                     Recipient = cmd.Data.Recipient
                     Memo = cmd.Data.Memo
                  }
                  Status = DomesticTransferProgress.Outgoing
               }
      }

type ApproveDomesticTransferCommand = Command<DomesticTransferApproved>

module ApproveDomesticTransferCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (initiatedBy: InitiatedById)
      (data: DomesticTransferApproved)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

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
      (initiatedBy: InitiatedById)
      (data: DomesticTransferRejected)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

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
      (initiatedBy: InitiatedById)
      (data: DomesticTransferProgressUpdate)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

   let toEvent
      (cmd: UpdateDomesticTransferProgressCommand)
      : ValidationResult<BankEvent<DomesticTransferProgressUpdate>>
      =
      BankEvent.create<DomesticTransferProgressUpdate> cmd |> Ok

module DomesticTransferToCommand =
   let progress (txn: DomesticTransfer) (status: DomesticTransferProgress) =
      UpdateDomesticTransferProgressCommand.create
         (txn.Sender.AccountId, txn.Sender.OrgId)
         txn.TransferId
         txn.InitiatedBy
         {
            BaseInfo = {
               Sender = txn.Sender
               Recipient = txn.Recipient
               ScheduledDate = txn.ScheduledDate
               Amount = txn.Amount
               Memo = txn.Memo
            }
            Status = status
         }

   let approve (txn: DomesticTransfer) =
      ApproveDomesticTransferCommand.create
         (txn.Sender.AccountId, txn.Sender.OrgId)
         txn.TransferId
         txn.InitiatedBy
         {
            BaseInfo = {
               Sender = txn.Sender
               Recipient = txn.Recipient
               ScheduledDate = txn.ScheduledDate
               Amount = txn.Amount
               Memo = txn.Memo
            }
            Status = txn.Status
         }

   let reject (txn: DomesticTransfer) (reason: TransferDeclinedReason) =
      RejectDomesticTransferCommand.create
         (txn.Sender.AccountId, txn.Sender.OrgId)
         txn.TransferId
         txn.InitiatedBy
         {
            BaseInfo = {
               Sender = txn.Sender
               Recipient = txn.Recipient
               ScheduledDate = txn.ScheduledDate
               Amount = txn.Amount
               Memo = txn.Memo
            }
            Reason = reason
         }

   let retry (txn: DomesticTransfer) =
      Command.create
         (AccountId.toEntityId txn.Sender.AccountId)
         txn.Sender.OrgId
         txn.TransferId
         txn.InitiatedBy
         {
            ScheduledDate = txn.ScheduledDate
            Amount = txn.Amount
            Sender = txn.Sender
            Recipient = txn.Recipient
            Memo = txn.Memo
         }

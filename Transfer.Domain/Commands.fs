namespace Bank.Transfer.Domain

open Validus
open System

open Lib.SharedTypes
open Lib.Validators

type InternalTransferWithinOrgCommand =
   Command<InternalTransferWithinOrgPending>

module InternalTransferWithinOrgCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: InternalTransferWithinOrgPending)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent
      (cmd: InternalTransferWithinOrgCommand)
      : ValidationResult<BankEvent<InternalTransferWithinOrgPending>>
      =
      validate {
         let input = cmd.Data.BaseInfo
         let! _ = amountValidator "Transfer amount" input.Amount

         let! _ = dateNotDefaultValidator "Transfer date" input.ScheduledDate

         return BankEvent.create<InternalTransferWithinOrgPending> cmd
      }

type ApproveInternalTransferWithinOrgCommand =
   Command<InternalTransferWithinOrgApproved>

module ApproveInternalTransferWithinOrgCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (initiatedBy: InitiatedById)
      (data: InternalTransferWithinOrgApproved)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

   let toEvent
      (cmd: ApproveInternalTransferWithinOrgCommand)
      : ValidationResult<BankEvent<InternalTransferWithinOrgApproved>>
      =
      BankEvent.create<InternalTransferWithinOrgApproved> cmd |> Ok

type RejectInternalTransferWithinOrgCommand =
   Command<InternalTransferWithinOrgRejected>

module RejectInternalTransferWithinOrgCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (initiatedBy: InitiatedById)
      (data: InternalTransferWithinOrgRejected)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

   let toEvent
      (cmd: RejectInternalTransferWithinOrgCommand)
      : ValidationResult<BankEvent<InternalTransferWithinOrgRejected>>
      =
      BankEvent.create<InternalTransferWithinOrgRejected> cmd |> Ok

type InternalTransferBetweenOrgsCommand =
   Command<InternalTransferBetweenOrgsPending>

module InternalTransferBetweenOrgsCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: InternalTransferBetweenOrgsPending)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent
      (cmd: InternalTransferBetweenOrgsCommand)
      : ValidationResult<BankEvent<InternalTransferBetweenOrgsPending>>
      =
      validate {
         let input = cmd.Data.BaseInfo
         let! _ = amountValidator "Transfer amount" input.Amount

         let! _ = dateNotDefaultValidator "Transfer date" input.ScheduledDate

         return BankEvent.create<InternalTransferBetweenOrgsPending> cmd
      }

type ApproveInternalTransferBetweenOrgsCommand =
   Command<InternalTransferBetweenOrgsApproved>

module ApproveInternalTransferBetweenOrgsCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (initiatedBy: InitiatedById)
      (data: InternalTransferBetweenOrgsApproved)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

   let toEvent
      (cmd: ApproveInternalTransferBetweenOrgsCommand)
      : ValidationResult<BankEvent<InternalTransferBetweenOrgsApproved>>
      =
      BankEvent.create<InternalTransferBetweenOrgsApproved> cmd |> Ok

type RejectInternalTransferBetweenOrgsCommand =
   Command<InternalTransferBetweenOrgsRejected>

module RejectInternalTransferBetweenOrgsCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (initiatedBy: InitiatedById)
      (data: InternalTransferBetweenOrgsRejected)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

   let toEvent
      (cmd: RejectInternalTransferBetweenOrgsCommand)
      : ValidationResult<BankEvent<InternalTransferBetweenOrgsRejected>>
      =
      BankEvent.create<InternalTransferBetweenOrgsRejected> cmd |> Ok

type DepositInternalTransferWithinOrgCommand =
   Command<InternalTransferWithinOrgDeposited>

module DepositInternalTransferWithinOrgCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (initiatedBy: InitiatedById)
      (data: InternalTransferWithinOrgDeposited)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

   let toEvent
      (cmd: DepositInternalTransferWithinOrgCommand)
      : ValidationResult<BankEvent<InternalTransferWithinOrgDeposited>>
      =
      BankEvent.create<InternalTransferWithinOrgDeposited> cmd |> Ok

type DepositInternalTransferBetweenOrgsCommand =
   Command<InternalTransferBetweenOrgsDeposited>

module DepositInternalTransferBetweenOrgsCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      correlationId
      (initiatedBy: InitiatedById)
      (data: InternalTransferBetweenOrgsDeposited)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

   let toEvent
      (cmd: DepositInternalTransferBetweenOrgsCommand)
      : ValidationResult<BankEvent<InternalTransferBetweenOrgsDeposited>>
      =
      BankEvent.create<InternalTransferBetweenOrgsDeposited> cmd |> Ok

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
            AccountNumber.fromString "Account Number" cmd.Data.AccountNumber

         and! routingNumber =
            RoutingNumber.fromString "Routing Number" cmd.Data.RoutingNumber

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
            AccountNumber.fromString "Account Number" cmd.Data.AccountNumber

         and! routingNumber =
            RoutingNumber.fromString "Routing Number" cmd.Data.RoutingNumber

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

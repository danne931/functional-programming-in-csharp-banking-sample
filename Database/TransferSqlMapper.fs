module TransferSqlMapper

open System

open Lib.SharedTypes
open OrganizationSqlMapper
open AccountSqlMapper
open Bank.Transfer.Domain

module Table =
   let transfer = "transfer"
   let internalTransfer = "transfer_internal"
   let domesticTransfer = "transfer_domestic"
   let domesticRecipient = "transfer_domestic_recipient"

module TransferTypeCast =
   let paymentNetwork = "payment_network"

   let domesticRecipientAccountDepository =
      "domestic_transfer_recipient_account_depository"

   let domesticRecipientStatus = "domestic_transfer_recipient_status"
   let domesticTransferStatus = "domestic_transfer_status"
   let internalTransferStatus = "internal_transfer_status"
   let transferCategory = "transfer_category"

module TransferFields =
   let transferId = "transfer_id"
   let initiatedById = "initiated_by_id"
   let amount = "amount"
   let transferCategory = "transfer_category"
   let scheduledAt = "scheduled_at"
   let senderOrgId = "sender_org_id"
   let senderAccountId = "sender_account_id"
   let memo = "memo"
   let createdAt = "created_at"

   // Specific to internal_transfer table
   module Internal =
      let status = "transfer_status"
      let statusDetail = "transfer_status_detail"
      let recipientOrgId = "recipient_org_id"
      let recipientAccountId = "recipient_account_id"

   // Specific to domestic_transfer table
   module Domestic =
      let status = "transfer_status"
      let statusDetail = "transfer_status_detail"
      let recipientAccountId = "recipient_account_id"

   // Specific to domestic_transfer_recipient table
   module DomesticRecipient =
      let accountId = "recipient_account_id"
      let firstName = "first_name"
      let lastName = "last_name"
      let nickname = "nickname"
      let routingNumber = "routing_number"
      let accountNumber = "account_number"
      let status = "recipient_status"
      let depository = "depository"
      let paymentNetwork = "payment_network"

module TransferSqlReader =
   let transferId (read: RowReader) =
      TransferFields.transferId |> read.uuid |> TransferId

   let initiatedById (read: RowReader) =
      TransferFields.initiatedById |> read.uuid |> EmployeeId |> InitiatedById

   let amount (read: RowReader) = TransferFields.amount |> read.decimal

   let transferCategory (read: RowReader) : TransferCategory =
      TransferFields.transferCategory
      |> read.string
      |> TransferCategory.fromStringUnsafe

   let scheduledAt (read: RowReader) =
      TransferFields.scheduledAt |> read.dateTime

   let senderOrgId (read: RowReader) =
      TransferFields.senderOrgId |> read.uuid |> OrgId

   let senderAccountId (read: RowReader) =
      TransferFields.senderAccountId |> read.uuid |> AccountId

   let memo (read: RowReader) = TransferFields.memo |> read.textOrNone

   let createdAt (read: RowReader) = read.dateTime TransferFields.createdAt

   module Internal =
      let status (read: RowReader) =
         TransferFields.Internal.statusDetail
         |> read.text
         |> Serialization.deserializeUnsafe<InternalTransferStatus>

      let recipientOrgId (read: RowReader) =
         TransferFields.Internal.recipientOrgId |> read.uuid |> OrgId

      let recipientAccountId (read: RowReader) =
         TransferFields.Internal.recipientAccountId |> read.uuid |> AccountId

   module DomesticRecipient =
      let accountId (read: RowReader) =
         TransferFields.DomesticRecipient.accountId |> read.uuid |> AccountId

      let firstName (read: RowReader) =
         TransferFields.DomesticRecipient.firstName |> read.string

      let lastName (read: RowReader) =
         TransferFields.DomesticRecipient.lastName |> read.string

      let nickname (read: RowReader) =
         TransferFields.DomesticRecipient.nickname |> read.stringOrNone

      let routingNumber = AccountSqlReader.routingNumber
      let accountNumber = AccountSqlReader.accountNumber

      let status (read: RowReader) =
         TransferFields.DomesticRecipient.status
         |> read.string
         |> RecipientRegistrationStatus.fromStringUnsafe

      let depository (read: RowReader) =
         TransferFields.DomesticRecipient.depository
         |> read.string
         |> DomesticRecipientAccountDepository.fromStringUnsafe

      let paymentNetwork (read: RowReader) =
         TransferFields.DomesticRecipient.paymentNetwork
         |> read.string
         |> PaymentNetwork.fromStringUnsafe

      let recipient (read: RowReader) : DomesticTransferRecipient = {
         FirstName = firstName read
         LastName = lastName read
         Nickname = nickname read
         AccountNumber = accountNumber read
         RoutingNumber = routingNumber read
         Status = status read
         AccountId = accountId read
         Depository = depository read
         PaymentNetwork = paymentNetwork read
         CreatedAt = createdAt read
      }

   module Domestic =
      let status (read: RowReader) =
         TransferFields.Domestic.statusDetail
         |> read.text
         |> Serialization.deserializeUnsafe<DomesticTransferProgress>

      let recipientAccountId (read: RowReader) =
         TransferFields.Domestic.recipientAccountId |> read.uuid |> AccountId

      let sender (read: RowReader) : DomesticTransferSender = {
         Name = AccountSqlReader.name read
         AccountNumber = read.int64 AccountFields.accountNumber |> AccountNumber
         RoutingNumber = read.int AccountFields.routingNumber |> RoutingNumber
         OrgId = AccountSqlReader.orgId read
         AccountId = AccountSqlReader.accountId read
      }

      let transfer (read: RowReader) : DomesticTransfer = {
         TransferId = transferId read
         InitiatedBy = initiatedById read
         Memo = memo read
         Status = status read
         Sender = sender read
         Recipient = DomesticRecipient.recipient read
         Amount = amount read
         ScheduledDate = scheduledAt read
      }

module TransferSqlWriter =
   let transferId = TransferId.get >> Sql.uuid
   let initiatedById = InitiatedById.get >> Sql.uuid
   let amount = Sql.decimal
   let transferCategory (cat: TransferCategory) = Sql.string (string cat)
   let scheduledAt (date: DateTime) = Sql.timestamptz date
   let senderOrgId = OrgSqlWriter.orgId
   let senderAccountId = AccountSqlWriter.accountId
   let memo = Sql.textOrNone
   let createdAt (date: DateTime) = Sql.timestamptz date

   module Internal =
      let status =
         function
         | InternalTransferStatus.Pending -> "Pending"
         | InternalTransferStatus.Approved -> "Approved"
         | InternalTransferStatus.Deposited -> "Deposited"
         | InternalTransferStatus.Failed _ -> "Failed"
         >> Sql.string

      let statusDetail (status: InternalTransferStatus) =
         status |> Serialization.serialize |> Sql.jsonb

      let recipientOrgId = OrgSqlWriter.orgId
      let recipientAccountId = AccountSqlWriter.accountId

   module Domestic =
      let status =
         function
         | DomesticTransferProgress.Outgoing -> "Outgoing"
         | DomesticTransferProgress.Complete -> "Complete"
         | DomesticTransferProgress.InProgress _ -> "InProgress"
         | DomesticTransferProgress.Failed _ -> "Failed"
         >> Sql.string

      let statusDetail (status: DomesticTransferProgress) =
         status |> Serialization.serialize |> Sql.jsonb

      let recipientAccountId = AccountSqlWriter.accountId

   module DomesticRecipient =
      let accountId = AccountSqlWriter.accountId
      let firstName = Sql.string
      let lastName = Sql.string
      let nickname = Sql.stringOrNone
      let routingNumber = AccountSqlWriter.routingNumber
      let accountNumber = AccountSqlWriter.accountNumber

      let status (status: RecipientRegistrationStatus) =
         Sql.string (string status)

      let depository (status: DomesticRecipientAccountDepository) =
         Sql.string (string status)

      let paymentNetwork (network: PaymentNetwork) = Sql.string (string network)

module Query =
   let domesticTransfer =
      $"""
      SELECT
         dt.{TransferFields.Domestic.recipientAccountId},
         dt.{TransferFields.Domestic.status},
         dt.{TransferFields.Domestic.statusDetail},
         t.{TransferFields.transferId},
         t.{TransferFields.initiatedById},
         t.{TransferFields.scheduledAt},
         t.{TransferFields.senderOrgId},
         t.{TransferFields.senderAccountId},
         t.{TransferFields.amount},
         t.{TransferFields.memo},
         dr.{TransferFields.DomesticRecipient.firstName},
         dr.{TransferFields.DomesticRecipient.lastName},
         dr.{TransferFields.DomesticRecipient.nickname},
         dr.{TransferFields.DomesticRecipient.accountNumber},
         dr.{TransferFields.DomesticRecipient.routingNumber},
         dr.{TransferFields.DomesticRecipient.depository},
         dr.{TransferFields.DomesticRecipient.paymentNetwork},
         dr.{TransferFields.DomesticRecipient.status},
         dr.{TransferFields.createdAt},
         a.{AccountFields.name},
         a.{AccountFields.accountNumber} as sender_account_number,
         a.{AccountFields.routingNumber} as sender_routing_number,
         a.{AccountFields.orgId},
         a.{AccountFields.accountId}
      FROM {Table.domesticTransfer} dt
      JOIN {Table.domesticRecipient} dr using({TransferFields.Domestic.recipientAccountId})
      JOIN {Table.transfer} t using({TransferFields.transferId})
      JOIN {AccountSqlMapper.table} a
         ON t.{TransferFields.senderAccountId} = a.{AccountFields.accountId}
      """

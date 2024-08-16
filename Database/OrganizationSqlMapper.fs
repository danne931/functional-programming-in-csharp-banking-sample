module OrganizationSqlMapper

open Lib.SharedTypes
open Bank.Account.Domain

let table = "organization"
let permissionsTable = "org_permissions"

module OrgFields =
   let orgId = "org_id"
   let name = "org_name"
   let requiresEmployeeInviteApproval = "requires_employee_invite_approval"
   let socialTransferDiscoveryAccountId = "social_transfer_discovery_account_id"

module OrgSqlReader =
   let orgId (read: RowReader) = OrgFields.orgId |> read.uuid |> OrgId
   let name (read: RowReader) = read.string OrgFields.name

   let requiresEmployeeInviteApproval (read: RowReader) =
      read.bool OrgFields.requiresEmployeeInviteApproval

   let socialTransferDiscoveryAccountId (read: RowReader) =
      OrgFields.socialTransferDiscoveryAccountId
      |> read.uuidOrNone
      |> Option.map AccountId

   let org (read: RowReader) : Org = {
      OrgId = orgId read
      Name = name read
      Permissions = {
         RequiresEmployeeInviteApproval = requiresEmployeeInviteApproval read
         SocialTransferDiscoveryPrimaryAccountId =
            socialTransferDiscoveryAccountId read
      }
   }

module OrgSqlWriter =
   let orgId (orgId: OrgId) =
      let (OrgId id) = orgId
      Sql.uuid id

   let name = Sql.string

   let requiresEmployeeInviteApproval = Sql.bool

   let socialTransferDiscoveryAccountId (accountId: AccountId option) =
      accountId |> Option.map AccountId.get |> Sql.uuidOrNone

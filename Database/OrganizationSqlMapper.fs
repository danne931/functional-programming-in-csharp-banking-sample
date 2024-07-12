module OrganizationSqlMapper

open Lib.SharedTypes

let table = "organization"

module OrgFields =
   let orgId = "org_id"
   let name = "org_name"
   let requiresEmployeeInviteApproval = "requires_employee_invite_approval"

module OrgSqlReader =
   let orgId (read: RowReader) = OrgFields.orgId |> read.uuid |> OrgId
   let name (read: RowReader) = read.string OrgFields.name

   let requiresEmployeeInviteApproval (read: RowReader) =
      read.bool OrgFields.requiresEmployeeInviteApproval

   let org (read: RowReader) = {
      OrgId = orgId read
      Name = name read
      Permissions = {
         RequiresEmployeeInviteApproval = requiresEmployeeInviteApproval read
      }
   }

module OrgSqlWriter =
   let orgId (orgId: OrgId) =
      let (OrgId id) = orgId
      Sql.uuid id

   let name = Sql.string

   let requiresEmployeeInviteApproval = Sql.bool

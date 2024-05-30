module OrganizationSqlMapper

open Lib.SharedTypes

let table = "organization"

module OrgFields =
   let orgId = "org_id"
   let name = "name"

module OrgSqlReader =
   let orgId (read: RowReader) = OrgFields.orgId |> read.uuid |> OrgId
   let name (read: RowReader) = read.string OrgFields.name

module OrgSqlWriter =
   let orgId (orgId: OrgId) =
      let (OrgId id) = orgId
      Sql.uuid id

   let name = Sql.string

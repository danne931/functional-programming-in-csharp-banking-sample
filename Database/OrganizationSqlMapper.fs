module OrganizationSqlMapper

let table = "organization"

module OrgFields =
   let orgId = "org_id"
   let name = "name"

module OrgSqlReader =
   let orgId (read: RowReader) = read.uuid OrgFields.orgId
   let name (read: RowReader) = read.string OrgFields.name

module OrgSqlWriter =
   let orgId = Sql.uuid
   let name = Sql.string

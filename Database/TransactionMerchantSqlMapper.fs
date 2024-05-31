module TransactionMerchantSqlMapper

open OrganizationSqlMapper
open Bank.Account.Domain

let table = "merchant"

module MerchantFields =
   let orgId = OrgFields.orgId
   let name = "name"
   let alias = "alias"

module MerchantSqlReader =
   let orgId = OrgSqlReader.orgId
   let name (read: RowReader) = read.string MerchantFields.name
   let alias (read: RowReader) = read.stringOrNone MerchantFields.alias

   let merchant (read: RowReader) : Merchant = {
      OrgId = orgId read
      Name = name read
      Alias = alias read
   }

module MerchantSqlWriter =
   let orgId = OrgSqlWriter.orgId
   let name = Sql.string
   let alias = Sql.stringOrNone

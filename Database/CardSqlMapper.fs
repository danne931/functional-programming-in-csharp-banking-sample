module CardSqlMapper

open System

open Bank.Employee.Domain
open Lib.SharedTypes
open EmployeeSqlMapper
open OrganizationSqlMapper
open AccountSqlMapper

let table = "card"

module CardTypeCast =
   let status = "card_status"
   let cardType = "card_type"

module CardFields =
   let cardNumberLast4 = "card_number_last_4"
   let dailyPurchaseLimit = "daily_purchase_limit"
   let monthlyPurchaseLimit = "monthly_purchase_limit"
   let isVirtual = "virtual"
   let cardType = "card_type"
   let status = "card_status"
   let cardNickname = "card_nickname"
   let lastPurchaseAt = "last_purchase_at"
   let expMonth = "exp_month"
   let expYear = "exp_year"
   let cardId = "card_id"
   let employeeId = EmployeeFields.employeeId
   let accountId = AccountFields.accountId
   let orgId = OrgFields.orgId
   let createdAt = "created_at"

module CardSqlReader =
   let cardId (read: RowReader) =
      CardFields.cardId |> read.uuid |> CardId

   let orgId = OrgSqlReader.orgId
   let employeeId = EmployeeSqlReader.employeeId
   let accountId = AccountSqlReader.accountId

   let cardNumberLast4 (read: RowReader) =
      read.string CardFields.cardNumberLast4

   let cardType (read: RowReader) =
      read.string CardFields.cardType |> CardType.fromStringUnsafe

   let status (read: RowReader) =
      read.string CardFields.status |> CardStatus.fromStringUnsafe

   let isVirtual (read: RowReader) = read.bool CardFields.isVirtual

   let dailyPurchaseLimit (read: RowReader) =
      read.decimal CardFields.dailyPurchaseLimit

   let monthlyPurchaseLimit (read: RowReader) =
      read.decimal CardFields.monthlyPurchaseLimit

   let lastPurchaseAt (read: RowReader) =
      read.dateTimeOrNone CardFields.lastPurchaseAt

   let cardNickname (read: RowReader) =
      read.stringOrNone CardFields.cardNickname

   let expMonth (read: RowReader) = read.int CardFields.expMonth
   let expYear (read: RowReader) = read.int CardFields.expYear

   let createdAt (read: RowReader) = read.dateTime CardFields.createdAt

   let card (read: RowReader) = {
      CardNumberLast4 = cardNumberLast4 read
      CardNickname = cardNickname read
      DailyPurchaseLimit = dailyPurchaseLimit read
      MonthlyPurchaseLimit = monthlyPurchaseLimit read
      CardId = cardId read
      AccountId = accountId read
      CardType = cardType read
      Virtual = isVirtual read
      Status = status read
      Expiration = {
         Month = expMonth read
         Year = expYear read
      }
      LastPurchaseAt = lastPurchaseAt read
   }

module CardSqlWriter =
   let cardId (cardId: CardId) =
      let (CardId id) = cardId
      Sql.uuid id

   let orgId = OrgSqlWriter.orgId
   let employeeId = EmployeeSqlWriter.employeeId
   let accountId = AccountSqlWriter.accountId

   let accountIds (ids: AccountId list) =
      ids |> List.map AccountId.get |> Array.ofList |> Sql.uuidArray

   let cardNumberLast4 = Sql.string

   let status (status: CardStatus) = Sql.string (string status)
   let cardType (cardType: CardType) = Sql.string (string cardType)

   let isVirtual = Sql.bool
   let dailyPurchaseLimit = Sql.decimal
   let monthlyPurchaseLimit = Sql.decimal
   let lastPurchaseAt (date: DateTime option) = Sql.timestamptzOrNone date
   let cardNickname = Sql.stringOrNone
   let expMonth = Sql.int
   let expYear = Sql.int
   let createdAt (date: DateTime) = Sql.timestamptz date

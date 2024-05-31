module CategorySqlMapper

let table = "category"

module CategoryFields =
   let catId = "category_id"
   let name = "name"

module CategorySqlReader =
   let catId (read: RowReader) = read.int CategoryFields.catId
   let name (read: RowReader) = read.string CategoryFields.name

module CategorySqlWriter =
   let catId = Sql.int
   let name = Sql.string

module User

open System

type User = {
   FirstName: string
   LastName: string
   Email: string
   AccountId: Guid
}

let pgMapper (read: RowReader) = {
   FirstName = read.text "first_name"
   LastName = read.text "last_name"
   Email = read.text "email"
   AccountId = read.uuid "account_id"
}

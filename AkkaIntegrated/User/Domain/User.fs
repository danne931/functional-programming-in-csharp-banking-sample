module User

open System

open Lib.Types

type User = {
   FirstName: string
   LastName: string
   Email: Email
   AccountId: Guid
}

type UserDto = {
   FirstName: string
   LastName: string
   Email: string
   AccountId: Guid
}

let toDto (user: User) : UserDto = {
   FirstName = user.FirstName
   LastName = user.LastName
   Email = string user.Email
   AccountId = user.AccountId
}

let pgMapper (read: RowReader) : User = {
   FirstName = read.text "first_name"
   LastName = read.text "last_name"
   Email = Email.deserialize <| read.text "email"
   AccountId = read.uuid "account_id"
}

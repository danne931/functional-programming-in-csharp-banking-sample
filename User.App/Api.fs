module Bank.User.Api

open FsToolkit.ErrorHandling

open Lib.Postgres
open Lib.SharedTypes
open User

/// <summary>
/// Get users for UI demonstration purposes.
/// Allows user to choose what account to process transactions on.
/// </summary>
let getUsers () = taskResultOption {
   let! users =
      pgQuery<User> "SELECT * FROM users" None (fun (read: RowReader) -> {
         FirstName = read.text "first_name"
         LastName = read.text "last_name"
         Email = Email.deserialize <| read.text "email"
         AccountId = read.uuid "account_id"
      })

   return List.map toDto users
}

let createUser (user: User) =
   let dto = toDto user

   pgPersist "INSERT into users \
      (first_name, last_name, email, account_id) \
      VALUES (@firstName, @lastName, @email, @accountId)" [
      "@firstName", Sql.text dto.FirstName
      "@lastName", Sql.text dto.LastName
      "@email", Sql.text dto.Email
      "@accountId", Sql.uuid dto.AccountId
   ]

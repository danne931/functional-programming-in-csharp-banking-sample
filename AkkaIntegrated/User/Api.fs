module Bank.User.Api

open FsToolkit.ErrorHandling

open Lib.Postgres
open User

/// <summary>
/// Get users for UI demonstration purposes.
/// Allows user to choose what account to process transactions on.
/// </summary>
let getUsers () = taskResultOption {
   let! users = pgQuery<User> "SELECT * FROM users" None User.pgMapper
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

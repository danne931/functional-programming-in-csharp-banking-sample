module Bank.User.Api

open Lib.Postgres
open User

/// <summary>
/// Get users for UI demonstration purposes.
/// Allows user to choose what account to process transactions on.
/// </summary>
let getUsers () =
   pgGet<User> "SELECT * FROM users" None User.pgMapper

let createUser (user: User) =
   pgPersist "INSERT into users \
      (first_name, last_name, email, account_id) \
      VALUES (@firstName, @lastName, @email, @accountId)" [
      "@firstName", Sql.text user.FirstName
      "@lastName", Sql.text user.LastName
      "@email", Sql.text user.Email
      "@accountId", Sql.uuid user.AccountId
   ]

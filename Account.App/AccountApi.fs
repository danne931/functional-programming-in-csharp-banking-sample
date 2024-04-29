module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open FsToolkit.ErrorHandling
open Validus

open Lib.SharedTypes
open Lib.Postgres
open Bank.Account.Domain
open AccountSqlMapper

let processCommand
   (system: ActorSystem)
   (command: AccountCommand)
   (entityId: Guid)
   (validation: ValidationResult<BankEvent<'E>>)
   =
   taskResult {
      let! _ = validation |> Result.mapError Err.ValidationError
      let ref = AccountActor.get system entityId
      ref <! AccountMessage.StateChange command
      return validation |> Result.map _.Id
   }

let getAccount (id: Guid) = taskResultOption {
   let! accountList =
      pgQuery<AccountState>
         "SELECT * FROM accounts WHERE id = @accountId"
         (Some [ "accountId", Sql.uuid id ])
         AccountSqlReader.account

   return accountList.Head
}

let getAccounts () =
   pgQuery<AccountState> "SELECT * FROM accounts" None AccountSqlReader.account

let getAccountsByIds (accountIds: Guid list) =
   pgQuery<AccountState>
      "SELECT * FROM accounts WHERE id = ANY(@accountIds)"
      (Some [ "accountIds", accountIds |> List.toArray |> Sql.uuidArray ])
      AccountSqlReader.account

// Diagnostic
let getAccountEventsFromAkka
   (sys: ActorSystem)
   (accountId: Guid)
   : AccountEvent list Task
   =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetEvents, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

// Diagnostic
let getAccountFromAkka
   (sys: ActorSystem)
   (accountId: Guid)
   : AccountState option Task
   =
   let ref = AccountActor.get sys accountId

   ref.Ask(AccountMessage.GetAccount, Some(TimeSpan.FromSeconds 3))
   |> Async.toTask

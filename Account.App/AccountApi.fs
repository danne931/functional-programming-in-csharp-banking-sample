module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open FsToolkit.ErrorHandling
open Validus

open Lib.Types
open Lib.Postgres
open Bank.Account.Domain

let processCommand
   (system: ActorSystem)
   (command: AccountCommand)
   (entityId: Guid)
   (validation: ValidationResult<BankEvent<'E>>)
   =
   taskResult {
      let! _ = Result.mapError ValidationError validation
      let ref = AccountActor.get system entityId
      ref <! AccountMessage.StateChange command
      return validation
   }

let getAccountEvents
   (sys: ActorSystem)
   (accountId: Guid)
   : AccountEvent list option Task
   =
   let ref = AccountActor.get sys accountId
   ref <? AccountMessage.GetEvents |> Async.toTask

let getAccount (sys: ActorSystem) (accountId: Guid) : AccountState option Task =
   let ref = AccountActor.get sys accountId
   ref <? AccountMessage.GetAccount |> Async.toTask

let upsertAccounts (accounts: AccountState list) =
   let sqlParams =
      accounts
      |> List.map (fun account ->
         let dto = account.toDto ()

         [
            "@id", Sql.uuid dto.Id
            "@email", Sql.text dto.Email
            "@firstName", Sql.text dto.FirstName
            "@lastName", Sql.text dto.LastName
            "@balance", Sql.money dto.Balance
         ])

   pgTransaction [
      "INSERT into accounts \
         (id, email, first_name, last_name, balance) \
         VALUES (@id, @email, @firstName, @lastName, @balance) \
         ON CONFLICT (id) \
         DO \
            UPDATE SET balance = @balance;",
      sqlParams
   ]

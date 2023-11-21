module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open FsToolkit.ErrorHandling
open Validus

open Lib.Types
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

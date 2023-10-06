module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open Akka.Streams
open Akka.Persistence
open FsToolkit.ErrorHandling
open Validus

open Lib.Types
open BankTypes

let processCommand
   (system: ActorSystem)
   (command: 'C :> Command)
   (validation: ValidationResult<BankEvent<'E>>)
   =
   taskResult {
      let! _ = Result.mapError ValidationError validation
      let ref = AccountActor.get system command.EntityId
      ref <! AccountMessage.StateChange command
      return validation
   }

let aggregateEvents
   (actorSystem: ActorSystem)
   (source: Dsl.Source<Query.EventEnvelope, _>)
   : AccountEvent list Task
   =
   source.RunAggregate(
      [],
      (fun acc envelope -> unbox envelope.Event :: acc),
      actorSystem.Materializer()
   )

let getAccountEvents
   (actorSystem: ActorSystem)
   (id: Guid)
   : AccountEvent list option Task
   =
   task {
      let! evts =
         ActorUtil
            .readJournal(actorSystem)
            .CurrentEventsByPersistenceId(string id, 0, System.Int64.MaxValue)
         |> aggregateEvents actorSystem

      return if evts.IsEmpty then None else evts |> List.rev |> Some
   }

let getAccount (sys: ActorSystem) (accountId: Guid) : AccountState option Task =
   let ref = AccountActor.get sys accountId
   ref <? AccountMessage.Lookup |> Async.toTask

let diagnosticDelete (sys: ActorSystem) accountId = task {
   let ref = AccountActor.get sys accountId
   ref <! AccountMessage.Delete
   return accountId
}

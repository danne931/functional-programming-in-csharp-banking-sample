module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open Akka.Streams
open Akka.Persistence
open ActorUtil
open FsToolkit.ErrorHandling
open Validus

open Lib.Types
open BankTypes
open Bank.Account.Domain

let createAccount (fac: AccountActorFac) (cmd: CreateAccountCommand) = taskResult {
   let! _ = cmd.toEvent () |> Result.mapError ValidationError
   fac.tell cmd.EntityId <| AccountMessage.InitAccount cmd
   return cmd.EntityId
}

let processCommand
   (fac: AccountActorFac)
   (command: 'C :> Command)
   (validation: ValidationResult<BankEvent<'E>>)
   =
   taskResult {
      let! _ = Result.mapError ValidationError validation
      fac.tell command.EntityId <| AccountMessage.StateChange command
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
      let persistenceId = $"account/shardid/{string id}"

      let! evts =
         ActorUtil
            .readJournal(actorSystem)
            .CurrentEventsByPersistenceId(
               persistenceId,
               0,
               System.Int64.MaxValue
            )
         |> aggregateEvents actorSystem

      return if evts.IsEmpty then None else evts |> List.rev |> Some
   }

let getAccount
   (fac: AccountActorFac)
   (accountId: Guid)
   : AccountState option Task
   =
   fac.ask accountId AccountMessage.Lookup |> Async.toTask

let diagnosticDelete (fac: AccountActorFac) accountId = task {
   fac.tell accountId AccountMessage.Delete
   return accountId
}

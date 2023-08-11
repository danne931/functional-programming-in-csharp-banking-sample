module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open Akka.Streams
open Akka.Persistence
open ActorUtil

open Lib.Types
open BankTypes
open Bank.Account.Domain

let createAccount
   (fac: AccountActorFac)
   (validate: Validator<CreateAccountCommand>)
   (cmd: CreateAccountCommand)
   =
   cmd
   |> validate
   |> Result.map (fun _ ->
      fac.tell cmd.EntityId <| AccountMessage.InitAccount cmd

      cmd.EntityId)
   |> Task.FromResult

let processCommand (fac: AccountActorFac) (validate: Validator<'t>) cmd =
   cmd
   |> validate
   |> Result.map (fun _ ->
      fac.tell cmd.EntityId <| AccountMessage.StateChange cmd

      cmd.EntityId)
   |> Task.fromResult

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

let getAccount (fac: AccountActorFac) (accountId: Guid) = task {
   let! (accountOpt: AccountState option) =
      fac.ask accountId AccountMessage.Lookup |> Async.toTask

   return accountOpt
}

let diagnosticDelete (fac: AccountActorFac) accountId =
   fac.tell accountId AccountMessage.Delete
   Task.fromResult accountId

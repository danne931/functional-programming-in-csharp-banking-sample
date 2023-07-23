module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open Akka.Streams
open Akka.Persistence

open BankTypes
open Bank.Account.Domain
open Lib.Types

let createAccount
   (accounts: IActorRef<AccountCoordinatorMessage>)
   (validate: Validator<CreateAccountCommand>)
   (command: CreateAccountCommand)
   =
   command
   |> validate
   |> Result.map (fun _ ->
      let msg = AccountCoordinatorMessage.InitAccount command
      retype accounts <! msg.consistentHash ()
      command.EntityId)
   |> Task.FromResult

let processCommand
   (accounts: IActorRef<AccountCoordinatorMessage>)
   (validate: Validator<'t>)
   command
   =
   command
   |> validate
   |> Result.map (fun _ ->
      let msg = AccountCoordinatorMessage.StateChange command
      retype accounts <! msg.consistentHash ()
      command.EntityId)
   |> Task.fromResult

let aggregateEvents
   (actorSystem: ActorSystem)
   (source: Dsl.Source<Query.EventEnvelope, _>)
   : AccountEvent list Task
   =
   source.RunAggregate(
      [],
      (fun acc envelope ->
         let (Event evt) = unbox envelope.Event
         evt :: acc),
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

let getAccount
   (getAccountEvents: Guid -> AccountEvent list option Task)
   (accountId: Guid)
   =
   task {
      let! evtsOption = getAccountEvents accountId
      return Option.map Account.foldEventsIntoAccount evtsOption
   }

let softDeleteEvents
   (accounts: IActorRef<AccountCoordinatorMessage>)
   accountId
   =
   let msg = AccountCoordinatorMessage.Delete accountId
   retype accounts <! msg.consistentHash ()
   Task.fromResult accountId

/// <summary>
/// TODO: Will not work once persistence snapshotting is enabled.
///       Refactor to pull user records instead.
/// Get all created account events for UI demonstration purposes.
/// Allows user to choose what account to process transactions on.
/// </summary>
let getAccountCreatedEvents actorSystem = task {
   let! evts =
      ActorUtil.readJournal(actorSystem).CurrentEventsByTag("CreatedAccount")
      |> aggregateEvents actorSystem

   return if evts.IsEmpty then None else Some evts
}

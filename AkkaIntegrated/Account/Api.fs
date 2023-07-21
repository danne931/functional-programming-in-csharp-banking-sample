module Bank.Account.Api

open System
open System.Threading.Tasks
open FSharp.Control
open Akkling
open Akka.Actor
open Akka.Streams

open BankTypes
open Bank.Account.Domain
open Lib.Types

let processCommand
   (accounts: IActorRef<AccountCoordinatorMessage>)
   (validate: Validator<'t>)
   command
   =
   command
   |> validate
   |> Result.map (fun _ ->
      let msg = AccountCoordinatorMessage.StateChange command
      retype accounts <! msg.consistentHash ())
   |> Task.fromResult

let getAccountEvents
   (actorSystem: ActorSystem)
   (id: Guid)
   : AccountEvent list option Task
   =
   task {
      let! events =
         ActorUtil
            .readJournal(actorSystem)
            .CurrentEventsByPersistenceId(string id, 0, System.Int64.MaxValue)
            .RunAggregate(
               [],
               (fun acc envelope ->
                  let (Event evt) = unbox envelope.Event
                  evt :: acc),
               actorSystem.Materializer()
            )

      return if events.IsEmpty then None else events |> List.rev |> Some
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
/// Get all account IDs for UI demonstration purposes.
/// Allows user to choose what account to process transactions on.
/// </summary>
let getAccountIds actorSystem = task {
   let! ids =
      ActorUtil
         .readJournal(actorSystem)
         .CurrentPersistenceIds()
         .RunAggregate(
            [],
            (fun acc id -> Guid id :: acc),
            actorSystem.Materializer()
         )

   return if ids.IsEmpty then None else Some ids
}

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

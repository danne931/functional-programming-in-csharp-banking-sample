[<RequireQualifiedAccess>]
module AccountClosureActor

open System
open Akka.Hosting
open Akka.Actor
open Akka.Persistence
open Akkling
open Akkling.Persistence
open FsToolkit.ErrorHandling

open Lib.Types
open ActorUtil
open Bank.AccountClosure.Api
open Bank.Account.Domain

let initState: Map<Guid, AccountState> = Map.empty

let actorProps
   (schedulingActorRef: IActorRef<SchedulingActor.Message>)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (emailRef: IActorRef<EmailActor.EmailMessage>)
   (deleteHistoricalRecords: Guid list -> TaskResultOption<Email list, Err>)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logInfo, logError = logInfo mailbox, logError mailbox

      let deleteHistoricalRecords accountIds = async {
         let! res = deleteHistoricalRecords accountIds |> Async.AwaitTask
         return DeleteHistoricalRecordsResponse res
      }

      let rec loop (accounts: Map<Guid, AccountState>) = actor {
         let! msg = mailbox.Receive()

         return!
            match box msg with
            | :? SnapshotOffer as o -> loop <| unbox o.Snapshot
            | :? AccountClosureMessage as msg ->
               match msg with
               | GetRegisteredAccounts ->
                  mailbox.Sender() <! accounts
                  ignored ()
               | Register account ->
                  let newState = Map.add account.EntityId account accounts

                  logInfo
                     $"""
                     Account scheduled for deletion - {account.EntityId}.
                     Total scheduled: {newState.Count}.
                     """

                  loop newState <@> SaveSnapshot newState
               | DeleteHistoricalRecordsResponse res ->
                  match res with
                  | Error e ->
                     logError $"Error deleting users & billing history {e}"
                     unhandled ()
                  | Ok opt ->
                     match opt with
                     | None ->
                        logError
                           "Account closure went awry.  No records deleted."

                        unhandled ()
                     | Some res ->
                        logInfo $"Account closure finished {res}"
                        ignored ()
               | ReverseClosure accountId ->
                  logInfo $"Reverse pending account closure for {accountId}"
                  let newState = Map.remove accountId accounts
                  loop newState <@> SaveSnapshot newState
               | ScheduleDeleteAll ->
                  if accounts.IsEmpty then
                     logInfo "AccountClosure - no accounts requested closure."
                     ignored ()
                  else
                     for account in accounts.Values do
                        // Delete event sourcing data immediately.
                        getAccountRef account.EntityId <! AccountMessage.Delete

                        emailRef <! EmailActor.AccountClose account

                     let accountIds = accounts |> Map.keys |> List.ofSeq

                     logInfo
                        $"Scheduling deletion of billing records for accounts: {accountIds}"
                     // Schedule deletion of historical/legal records for 3 months later.
                     schedulingActorRef
                     <! SchedulingActor.DeleteAccountsJobSchedule accountIds

                     loop initState <@> SaveSnapshot initState
               | DeleteAll accountIds ->
                  deleteHistoricalRecords accountIds |!> retype mailbox.Self
                  ignored ()
            | LifecycleEvent _ -> ignored ()
            | :? Akka.Persistence.RecoveryCompleted -> ignored ()
            | :? PersistentLifecycleEvent as _ -> ignored ()
            | :? SaveSnapshotSuccess -> ignored ()
            | :? SaveSnapshotFailure as e ->
               logError $"SaveSnapshotFailure {e.Cause}"
               unhandled ()
            | msg ->
               logError $"Unknown message {msg}"
               unhandled ()
      }

      loop initState

   propsPersist handler

let initProps
   (system: ActorSystem)
   (schedulingActorRef: IActorRef<SchedulingActor.Message>)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   actorProps
      schedulingActorRef
      getAccountRef
      (EmailActor.get system)
      deleteHistoricalRecords

let get (system: ActorSystem) : IActorRef<AccountClosureMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.AccountClosureMarker>()

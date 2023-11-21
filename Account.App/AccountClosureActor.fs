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

let initState = List.empty<AccountState>

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

      let rec loop (accounts: AccountState list) = actor {
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
                  let newState = account :: accounts

                  logInfo
                     $"""
                     Account scheduled for deletion - {account.EntityId}.
                     Total scheduled: {newState.Length}.
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
               | ScheduleDeleteAll ->
                  if accounts.IsEmpty then
                     logInfo "AccountClosure - no accounts requested closure."
                     ignored ()
                  else
                     let accountIds =
                        accounts
                        |> List.map (fun acct ->
                           // Delete event sourcing data immediately.
                           getAccountRef acct.EntityId <! AccountMessage.Delete

                           emailRef <! EmailActor.AccountClose acct

                           acct.EntityId)

                     logInfo
                        $"Scheduling deletion of billing records for accounts: {accountIds}"
                     // Schedule deletion of historical/legal records for 3 months later.
                     schedulingActorRef
                     <! SchedulingActor.DeleteAccountsJobSchedule accountIds

                     loop initState <@> SaveSnapshot initState
               | DeleteAll accountIds ->
                  deleteHistoricalRecords accountIds |!> retype mailbox.Self
                  ignored ()
               | ReverseClosure _ -> ignored () // TODO
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

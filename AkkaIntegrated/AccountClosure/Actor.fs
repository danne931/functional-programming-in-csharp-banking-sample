[<RequireQualifiedAccess>]
module AccountClosureActor

open System
open Akka.Hosting
open Akka.Actor
open Akka.Persistence
open Akka.Quartz.Actor.Commands
open Akkling
open Akkling.Persistence
open Quartz
open FsToolkit.ErrorHandling

open BankTypes
open Lib.Types
open ActorUtil
open Bank.AccountClosure.Api

// TODO: ScheduleDeleteAll & DeleteAll are commented out until
//       Quartz deserialization is configured to detect union types.
//       Using the separately declared types below for messages
//       passed to Quartz until then.
type AccountClosureMessage =
   | Register of AccountState
   //| ScheduleDeleteAll
   //| DeleteAll of Guid list
   | ReverseClosure of Guid
   | Lookup
   | DeleteHistoricalRecordsResponse of Result<Email list option, Err>

type ScheduleDeleteAll() =
   class
   end

type DeleteAll(accountIds: Guid list) =
   member x.AccountIds = accountIds

let private deletionJob (accountIds: Guid list) =
   let name = "AccountClosureDeletion"
   let group = "Bank"

   let trigger =
      TriggerBuilder
         .Create()
         .ForJob($"{name}Job", group)
         .WithIdentity($"{name}Trigger", group)
         .WithDescription(
            "Delete remaining user data 3 months after requested account closure"
         )
#if DEBUG
         .StartNow()
#else
         .StartAt(DateTimeOffset(DateTime.Now).AddDays(90))
#endif
         .Build()

   let path = ActorMetadata.accountClosure.Path.Value
   CreatePersistentJob(path, DeleteAll accountIds, trigger)

let initState = List.empty<AccountState>

let actorProps
   (quartzPersistentActorRef: IActorRef)
   (getAccountRef: EntityRefGetter<obj>)
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
            | :? ScheduleDeleteAll ->
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

                  // Schedule deletion of historical/legal records for 3 months later.
                  quartzPersistentActorRef.Tell(
                     deletionJob accountIds,
                     ActorRefs.Nobody
                  )

                  loop initState <@> SaveSnapshot initState

            | :? DeleteAll as o ->
               deleteHistoricalRecords o.AccountIds |!> retype mailbox.Self
               ignored ()
            | :? AccountClosureMessage as msg ->
               match msg with
               | Lookup ->
                  mailbox.Sender() <! accounts
                  ignored ()
               | Register account ->
                  let newState = account :: accounts
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

let start
   (system: ActorSystem)
   (quartzPersistentActorRef: IActorRef)
   (getAccountRef: EntityRefGetter<obj>)
   =
   spawn system ActorMetadata.accountClosure.Name
   <| actorProps
         quartzPersistentActorRef
         getAccountRef
         (EmailActor.get system)
         deleteHistoricalRecords

let scheduleNightlyCheck (quartzPersistentActorRef: IActorRef) =
   let name = "AccountClosureNightlyCheck"
   let group = "Bank"

   let trigger =
      TriggerBuilder
         .Create()
         .ForJob($"{name}Job", group)
         .WithIdentity($"{name}Trigger", group)
         .WithDescription("Nightly check for requested account closures")
#if DEBUG
         .WithSimpleSchedule(fun s ->
            s.WithIntervalInMinutes(3).RepeatForever() |> ignore)
#else
         .WithCronSchedule("0 30 10 ? * * *") // Every night at 10:30PM
#endif
         .Build()

   let path = ActorMetadata.accountClosure.Path.Value
   let job = CreatePersistentJob(path, ScheduleDeleteAll(), trigger)

   quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
   ()

let get (system: ActorSystem) : IActorRef<AccountClosureMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.AccountClosureMarker>()

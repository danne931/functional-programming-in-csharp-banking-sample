[<RequireQualifiedAccess>]
module AccountClosureActor

open System
open Akka.Actor
open Akka.Persistence
open Akka.Quartz.Actor.Commands
open Akkling
open Akkling.Persistence
open Quartz

open BankTypes
open ActorUtil
open Bank.AccountClosure.Api

// TODO: ScheduleDeleteAll & DeleteAll are commented out until
//       Quartz serialization is configured to detect union types.
//       Using the separately declared types below for messages
//       passed to Quartz until then.
type AccountClosureMessage =
   | Register of AccountState
   //| ScheduleDeleteAll
   //| DeleteAll of Guid list
   | ReverseClosure of Guid

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

let start (system: ActorSystem) (quartzPersistentActorRef: IActorRef) =
   let actorName = ActorMetadata.accountClosure.Name

   let handler (mailbox: Eventsourced<obj>) =
      let rec loop (accounts: AccountState list) = actor {
         let! msg = mailbox.Receive()
         let path = mailbox.Self.Path

         return!
            match box msg with
            | :? SnapshotOffer as o -> loop <| unbox o.Snapshot
            | :? ScheduleDeleteAll ->
               if accounts.IsEmpty then
                  printfn "AccountClosure - no accounts requested closure."
               else
                  let accountAref = AccountActorFac(mailbox.System)

                  let emailAref =
                     select mailbox (string ActorMetadata.email.Path.Value)

                  let accountIds =
                     accounts
                     |> List.map (fun acct ->
                        // Delete event sourcing data immediately.
                        accountAref.tell acct.EntityId AccountMessage.Delete

                        emailAref <! EmailActor.AccountClose acct

                        acct.EntityId)

                  // Schedule deletion of historical/legal records for 3 months later.
                  quartzPersistentActorRef.Tell(
                     deletionJob accountIds,
                     ActorRefs.Nobody
                  )

               ignored ()
            | :? DeleteAll as o ->
               let accountIds = o.AccountIds
               let emailsOpt = deleteHistoricalRecords(accountIds).Result

               if emailsOpt.IsNone then
                  printfn "Account closure went awry.  No records deleted."
                  unhandled ()
               else
                  printfn "Account closure finished %A" emailsOpt.Value
                  loop [] <@> SaveSnapshot []
            | :? AccountClosureMessage as msg ->
               match msg with
               | Register account ->
                  let newState = account :: accounts
                  loop newState <@> SaveSnapshot newState
            | LifecycleEvent _ -> ignored ()
            | :? Akka.Persistence.RecoveryCompleted -> ignored ()
            | :? PersistentLifecycleEvent as _ -> ignored ()
            | :? SaveSnapshotSuccess -> ignored ()
            | :? SaveSnapshotFailure as e ->
               printfn "SaveSnapshotFailure %A %A" e.Metadata path
               unhandled ()
            | msg ->
               printfn "Unknown message %A %A" msg path
               unhandled ()
      }

      loop []

   spawn system actorName <| propsPersist handler

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

   quartzPersistentActorRef.Tell(job, ActorRefs.NoSender)
   ()

[<RequireQualifiedAccess>]
module AccountClosureActor

open System
open Akka.Hosting
open Akka.Actor
open Akka.Persistence
open Akkling
open Akkling.Persistence
open Akka.Streams
open Akkling.Streams
open FsToolkit.ErrorHandling

open Lib.Types
open ActorUtil
open Bank.AccountClosure.Api
open Bank.Account.Domain

let deleteAccounts
   (system: ActorSystem)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (throttle: StreamThrottle)
   (accounts: Map<Guid, AccountState>)
   =
   Source.ofSeq accounts.Values
   |> Source.throttle
         ThrottleMode.Shaping
         throttle.Burst
         throttle.Count
         throttle.Duration
   |> Source.runForEach (system.Materializer()) (fun account ->
      getAccountRef account.EntityId <! AccountMessage.Delete)

let initState: Map<Guid, AccountState> = Map.empty

let actorProps
   (schedulingActorRef: IActorRef<SchedulingActor.Message>)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (getEmailRef: unit -> IActorRef<EmailActor.EmailMessage>)
   (deleteHistoricalRecords: Guid list -> TaskResultOption<Email list, Err>)
   (throttle: StreamThrottle)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logInfo, logError = logInfo mailbox, logError mailbox
      let deleteAccounts = deleteAccounts mailbox.System getAccountRef throttle

      let rec loop (accounts: Map<Guid, AccountState>) = actor {
         let! msg = mailbox.Receive()

         match box msg with
         | :? SnapshotOffer as o -> return! loop <| unbox o.Snapshot
         | :? AccountClosureMessage as msg ->
            match msg with
            | GetRegisteredAccounts -> mailbox.Sender() <! accounts
            | Register account ->
               let newState = Map.add account.EntityId account accounts

               logInfo
                  $"""
                  Account scheduled for deletion - {account.EntityId}.
                  Total scheduled: {newState.Count}.
                  """

               return!
                  match newState.Count % 10 with
                  | 0 -> loop newState <@> SaveSnapshot newState
                  | _ -> loop newState
            | ReverseClosure accountId ->
               logInfo $"Reverse pending account closure for {accountId}"
               let newState = Map.remove accountId accounts
               return! loop newState
            | ScheduleDeleteAll ->
               if accounts.IsEmpty then
                  logInfo "AccountClosure - no accounts requested closure."
               else
                  do! deleteAccounts accounts

                  for account in accounts.Values do
                     getEmailRef () <! EmailActor.AccountClose account

                  let accountIds = accounts |> Map.keys |> List.ofSeq

                  logInfo
                     $"Scheduling deletion of billing records for accounts: {accountIds}"
                  // Schedule deletion of historical/legal records for 3 months later.
                  schedulingActorRef
                  <! SchedulingActor.DeleteAccountsJobSchedule accountIds

                  return! loop initState <@> SaveSnapshot initState
            | DeleteAll accountIds ->
               let! res = deleteHistoricalRecords accountIds

               match res with
               | Error e ->
                  logError $"Error deleting users & billing history {e}"
                  return unhandled ()
               | Ok opt ->
                  match opt with
                  | None ->
                     logError "Account closure went awry.  No records deleted."

                     return unhandled ()
                  | Some res -> logInfo $"Account closure finished {res}"
         | msg ->
            return
               PersistentActorEventHandler.handleEvent
                  PersistentActorEventHandler.init
                  mailbox
                  msg
      }

      loop initState

   propsPersist handler

let initProps
   (system: ActorSystem)
   (schedulingActorRef: IActorRef<SchedulingActor.Message>)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (throttle: StreamThrottle)
   =
   actorProps
      schedulingActorRef
      getAccountRef
      (fun _ -> EmailActor.get system)
      deleteHistoricalRecords
      throttle

let get (system: ActorSystem) : IActorRef<AccountClosureMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.AccountClosureMarker>()

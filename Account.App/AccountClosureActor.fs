[<RequireQualifiedAccess>]
module AccountClosureActor

open Akka.Hosting
open Akka.Actor
open Akka.Persistence
open Akkling
open Akkling.Persistence
open Akkling.Cluster.Sharding
open Akka.Streams
open Akkling.Streams
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Lib.Types
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain

let deleteAccounts
   (system: ActorSystem)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (throttle: StreamThrottle)
   (accounts: Map<AccountId, Account>)
   =
   Source.ofSeq accounts.Values
   |> Source.throttle
         ThrottleMode.Shaping
         throttle.Burst
         throttle.Count
         throttle.Duration
   |> Source.runForEach (system.Materializer()) (fun account ->
      getAccountRef account.AccountId <! AccountMessage.Delete)

let initState: Map<AccountId, Account> = Map.empty

let actorProps
   (schedulingActorRef: IActorRef<SchedulingActor.Message>)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (getEmailRef: unit -> IActorRef<EmailActor.EmailMessage>)
   (deleteHistoricalRecords:
      AccountId list -> TaskResultOption<AccountNumber list, Err>)
   (throttle: StreamThrottle)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logInfo, logError = logInfo mailbox, logError mailbox
      let deleteAccounts = deleteAccounts mailbox.System getAccountRef throttle

      let rec loop (accounts: Map<AccountId, Account>) = actor {
         let! msg = mailbox.Receive()

         match box msg with
         | :? SnapshotOffer as o -> return! loop <| unbox o.Snapshot
         | :? AccountClosureMessage as msg ->
            match msg with
            | AccountClosureMessage.GetRegisteredAccounts ->
               mailbox.Sender() <! accounts
            | AccountClosureMessage.Register(account, initiatedBy) ->
               let newState = Map.add account.AccountId account accounts

               logInfo
                  $"""
                  Account scheduled for deletion - {account.AccountId}.
                  Total scheduled: {newState.Count}.
                  """

               // Deactivates this closed account in the sender account's
               // TransferRecipients Map.
               for sender in account.InternalTransferSenders.Values do
                  let msg =
                     DeactivateInternalRecipientCommand.create
                        sender
                        initiatedBy
                        {
                           RecipientId = account.AccountId
                           RecipientName = account.Name
                        }
                     |> AccountCommand.DeactivateInternalRecipient
                     |> AccountMessage.StateChange

                  getAccountRef sender.AccountId <! msg

               return!
                  match newState.Count % 10 with
                  | 0 -> loop newState <@> SaveSnapshot newState
                  | _ -> loop newState
            | AccountClosureMessage.ScheduleDeleteAll ->
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
            | AccountClosureMessage.DeleteAll accountIds ->
               let! res = deleteHistoricalRecords accountIds

               match res with
               | Error e ->
                  logError $"Error deleting billing history {e}"
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

   let spawnChild =
      fun ctx ->
         spawn
            ctx
            $"{ActorMetadata.accountClosure.Name}-worker"
            (propsPersist handler)

   supervisorProps spawnChild <| Strategy.OneForOne(fun _ -> Directive.Resume)

open Lib.Postgres
open AccountSqlMapper

// These records are held onto for reporting and legal reasons
// for 3 months following an account closure.
//
// ON CASCADE DELETE is configured (see Infrastructure/Migrations/Bank.sql).
// Deletion of an account with a given ID deletes corresponding
// user & billing statements in the same transaction.
let deleteHistoricalRecords (accountIds: AccountId list) =
   if accountIds.IsEmpty then
      TaskResult.ok None
   else
      pgQuery<AccountNumber>
         $"DELETE FROM {AccountSqlMapper.table} 
           WHERE {AccountFields.accountId} = ANY(@accountIds) 
           RETURNING {AccountFields.accountNumber}"
         (Some [
            "@accountIds",
            accountIds
            |> List.map AccountId.get
            |> List.toArray
            |> Sql.uuidArray
         ])
         AccountSqlReader.accountNumber

let initProps
   (system: ActorSystem)
   (schedulingActorRef: IActorRef<SchedulingActor.Message>)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
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

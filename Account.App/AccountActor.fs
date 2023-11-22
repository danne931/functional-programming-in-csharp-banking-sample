[<RequireQualifiedAccess>]
module AccountActor

open System
open System.Threading.Tasks
open Akka.Actor
open Akka.Persistence
open Akka.Streams
open Akkling
open Akkling.Persistence
open Akkling.Cluster.Sharding
open FsToolkit.ErrorHandling

open Lib.Types
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.User.Api
open BillingStatement
open User

let private persist e =
   e |> Event |> box |> Persist :> Effect<_>

let actorProps
   (persistence: AccountPersistence)
   (broadcaster: SignalRBroadcast)
   (getOrStartInternalTransferActor:
      Actor<_> -> IActorRef<BankEvent<TransferPending>>)
   (getDomesticTransferActor:
      ActorSystem -> IActorRef<DomesticTransferRecipientActor.Message>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   (getAccountClosureActor: ActorSystem -> IActorRef<AccountClosureMessage>)
   (getBillingCycleActor: ActorSystem -> IActorRef<BillingMessage>)
   (userPersistence: UserPersistence)
   =
   let createUser (user: User) (evt: BankEvent<CreatedAccount>) = async {
      let! res = userPersistence.createUser user |> Async.AwaitTask
      return UserCreationResponse(res, evt)
   }

   let getTransactions (account: AccountState) = async {
      return! persistence.getEvents account.EntityId |> Async.AwaitTask
   }

   let handler (mailbox: Eventsourced<obj>) =
      let logError, logWarning = logError mailbox, logWarning mailbox

      let rec loop (accountOpt: AccountState option) = actor {
         let! msg = mailbox.Receive()
         let account = Option.defaultValue AccountState.empty accountOpt

         match box msg with
         | Persisted mailbox e ->
            let (AccountMessage.Event evt) = unbox e
            let newState = Account.applyEvent account evt
            broadcaster.accountEventPersisted (evt, newState) |> ignore

            match evt with
            | CreatedAccount e ->
               let (user: User) = {
                  FirstName = e.Data.FirstName
                  LastName = e.Data.LastName
                  AccountId = e.EntityId
                  Email = e.Data.Email
               }

               createUser user e |!> retype mailbox.Self
            | TransferPending e -> mailbox.Self <! DispatchTransfer e
            | TransferDeposited e ->
               getEmailActor mailbox.System
               <! EmailActor.TransferDeposited(e, newState)
            | AccountClosed _ ->
               getAccountClosureActor mailbox.System
               <! AccountClosureMessage.Register newState
            | _ -> ()

            return! loop <| Some newState
         | :? SnapshotOffer as o -> return! loop <| Some(unbox o.Snapshot)
         | :? AccountMessage as msg ->
            match msg with
            | GetAccount -> mailbox.Sender() <! accountOpt
            | GetEvents ->
               match accountOpt with
               | None -> mailbox.Sender() <! None
               | Some account -> mailbox.Sender() <!| getTransactions account
            | StateChange cmd ->
               let validation = Account.stateTransition account cmd

               match validation with
               | Error err ->
                  let errMsg = string err
                  broadcaster.accountEventValidationFail errMsg |> ignore
                  logWarning $"Validation fail %s{errMsg}"

                  match err with
                  | StateTransitionError e ->
                     match e with
                     | InsufficientBalance _
                     | ExceededDailyDebit _ ->
                        getEmailActor mailbox.System
                        <! EmailActor.DebitDeclined(errMsg, account)
                     | _ -> ()
                  | _ -> ()

               | Ok(event, _) -> return persist event
            | DispatchTransfer evt ->
               match evt.Data.Recipient.AccountEnvironment with
               | RecipientAccountEnvironment.Internal ->
                  getOrStartInternalTransferActor mailbox <! evt
               | RecipientAccountEnvironment.Domestic ->
                  getDomesticTransferActor mailbox.System
                  <! (evt |> DomesticTransferRecipientActor.TransferPending)
               | _ -> ()
            | UserCreationResponse(res: Result<int, Err>, _) ->
               match res with
               | Ok _ ->
                  getEmailActor mailbox.System <! EmailActor.AccountOpen account
               | Error e ->
                  logError $"Error creating user {e}"
                  return unhandled ()
            | Delete ->
               let newState = {
                  account with
                     Status = AccountStatus.ReadyForDelete
               }

               return! loop (Some newState) <@> DeleteMessages Int64.MaxValue
            | BillingCycle when
               accountOpt.IsSome && not accountOpt.Value.CanProcessTransactions
               ->
               logWarning
                  "Account not able to process txns. Ignore billing cycle."

               return ignored ()
            | BillingCycle when accountOpt.IsSome ->
               let! (eventsOpt: AccountEvent list option) =
                  getTransactions account

               let accountId = account.EntityId

               if eventsOpt.IsNone then
                  logWarning "No transactions found for billing cycle."
               else
                  let txns =
                     List.choose
                        Account.accountEventToBillingTransaction
                        eventsOpt.Value

                  let billing =
                     BillingStatement.create
                        {|
                           AccountId = accountId
                           Name = account.Name
                           Balance = account.Balance
                        |}
                        txns

                  getBillingCycleActor mailbox.System
                  <! RegisterBillingStatement billing

                  // Maintenance fee conditionally applied after account transactions
                  // have been consolidated. If applied, it will be the first transaction
                  // of the new billing cycle.
                  mailbox.Self <! AccountMessage.BillingCycleEnd

                  let criteria = account.MaintenanceFeeCriteria

                  if
                     criteria.QualifyingDepositFound
                     || criteria.DailyBalanceThreshold
                  then
                     let msg =
                        SkipMaintenanceFeeCommand(accountId, criteria)
                        |> (StateChange << SkipMaintenanceFee)

                     mailbox.Self <! msg
                  else
                     let msg =
                        MaintenanceFeeCommand accountId
                        |> (StateChange << MaintenanceFee)

                     mailbox.Self <! msg

                  getEmailActor mailbox.System
                  <! EmailActor.BillingStatement account
            | AccountMessage.BillingCycleEnd -> return SaveSnapshot account
         // Event replay on actor start
         | :? AccountEvent as e when mailbox.IsRecovering() ->
            return! loop <| Some(Account.applyEvent account e)
         | LifecycleEvent _ -> return ignored ()
         | :? Akka.Persistence.RecoveryCompleted -> return ignored ()
         | :? Akka.Persistence.DeleteMessagesSuccess ->
            if account.Status = AccountStatus.ReadyForDelete then
               return DeleteSnapshots(SnapshotSelectionCriteria Int64.MaxValue)
         | :? Akka.Persistence.DeleteMessagesFailure as e ->
            logError $"Failure to delete message history %s{e.Cause.Message}"
            return unhandled ()
         | :? PersistentLifecycleEvent as e ->
            match e with
            | ReplaySucceed -> return ignored ()
            | ReplayFailed(exn, _) ->
               failwith $"Persistence replay failed: %s{exn.Message}"
            | PersistRejected(exn, _, _)
            | PersistFailed(exn, _, _) ->
               broadcaster.accountEventPersistenceFail exn.Message |> ignore
               failwith $"Persistence failed: %s{exn.Message}"
         | :? SaveSnapshotSuccess as res ->
            let sequenceNr = res.Metadata.SequenceNr

            return
               DeleteSnapshots(SnapshotSelectionCriteria(sequenceNr - 1L))
               <@> DeleteMessages sequenceNr
         | :? SaveSnapshotFailure as e ->
            logError $"SaveSnapshotFailure {e.Metadata}"
            return unhandled ()
         | :? DeleteSnapshotsSuccess ->
            if account.Status = AccountStatus.ReadyForDelete then
               return passivate ()
         | :? DeleteSnapshotsFailure as e ->
            logError $"DeleteSnapshotsFailure %s{e.Cause.Message}"
            return unhandled ()
         | msg ->
            logError $"Unknown message {msg}"
            return unhandled ()
      }

      loop None

   propsPersist handler

let get (sys: ActorSystem) (entityId: Guid) : IEntityRef<AccountMessage> =
   getEntityRef sys ClusterMetadata.accountShardRegion entityId

let private getAccountEvents
   (actorSystem: ActorSystem)
   (id: Guid)
   : AccountEvent list option Task
   =
   task {
      let! evts =
         ActorUtil
            .readJournal(actorSystem)
            .CurrentEventsByPersistenceId(string id, 0, System.Int64.MaxValue)
            .RunAggregate(
               [],
               (fun acc envelope -> unbox envelope.Event :: acc),
               actorSystem.Materializer()
            )

      return if evts.IsEmpty then None else evts |> List.rev |> Some
   }

let initProps (broadcaster: SignalRBroadcast) (system: ActorSystem) =
   let getOrStartInternalTransferActor mailbox =
      InternalTransferRecipientActor.getOrStart mailbox <| get system

   actorProps
      { getEvents = getAccountEvents system }
      broadcaster
      getOrStartInternalTransferActor
      DomesticTransferRecipientActor.get
      EmailActor.get
      AccountClosureActor.get
      BillingCycleActor.get
      { createUser = createUser }

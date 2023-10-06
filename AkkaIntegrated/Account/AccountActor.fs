[<RequireQualifiedAccess>]
module AccountActor

open System
open Akka.Actor
open Akka.Persistence
open Akkling
open Akkling.Persistence
open Akkling.Cluster.Sharding
open FsToolkit.ErrorHandling

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain

let private persist e =
   e |> Event |> box |> Persist :> Effect<_>

let actorProps
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (getOrStartInternalTransferActor:
      Actor<_> -> IActorRef<BankEvent<TransferPending>>)
   (getDomesticTransferActor:
      ActorSystem -> IActorRef<DomesticTransferRecipientActor.Message>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   (getAccountClosureActor:
      ActorSystem -> IActorRef<AccountClosureActor.AccountClosureMessage>)
   (startBillingCycleActor:
      Actor<_>
         -> AccountPersistence
         -> AccountState
         -> IActorRef<BillingCycleActor.Message>)
   (userPersistence: UserPersistence)
   =
   let createUser (user: User.User) (evt: BankEvent<CreatedAccount>) = async {
      let! res = userPersistence.createUser user |> Async.AwaitTask
      return UserCreationResponse(res, evt)
   }

   let handler (mailbox: Eventsourced<obj>) =
      let logError, logWarning = logError mailbox, logWarning mailbox

      let rec loop (accountOpt: AccountState option) = actor {
         let! msg = mailbox.Receive()
         let account = Option.defaultValue AccountState.empty accountOpt

         return!
            match box msg with
            | Persisted mailbox e ->
               let (Event evt) = unbox e
               let newState = Account.applyEvent account evt
               broadcaster.broadcast (evt, newState) |> ignore

               match evt with
               | CreatedAccount e ->
                  let (user: User.User) = {
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
                  <! AccountClosureActor.Register newState
               | _ -> ()

               loop (Some newState)
            | :? SnapshotOffer as o -> loop <| Some(unbox o.Snapshot)
            | :? AccountMessage as msg ->
               match msg with
               | Lookup ->
                  mailbox.Sender() <! accountOpt
                  ignored ()
               | BillingCycle _ when accountOpt.IsSome ->
                  ignored <| startBillingCycleActor mailbox persistence account
               | StateChange cmd ->
                  let validation = Account.stateTransition account cmd

                  match validation with
                  | Error err ->
                     let errMsg = string err
                     broadcaster.broadcastError errMsg |> ignore
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

                     ignored ()
                  | Ok(event, _) -> persist event
               | DispatchTransfer evt ->
                  match evt.Data.Recipient.AccountEnvironment with
                  | RecipientAccountEnvironment.Internal ->
                     getOrStartInternalTransferActor mailbox <! evt
                  | RecipientAccountEnvironment.Domestic ->
                     getDomesticTransferActor mailbox.System
                     <! (evt |> DomesticTransferRecipientActor.TransferPending)
                  | _ -> ()

                  ignored ()
               | UserCreationResponse(res: Result<int, Err>,
                                      evt: BankEvent<CreatedAccount>) ->
                  match res with
                  | Ok _ ->
                     getEmailActor mailbox.System
                     <! EmailActor.AccountOpen account

                     ignored ()
                  | Error e ->
                     logError $"Error creating user {e}"
                     unhandled ()
               | Delete ->
                  let newState = {
                     account with
                        Status = AccountStatus.ReadyForDelete
                  }

                  loop (Some newState) <@> DeleteMessages Int64.MaxValue
               | BillingCycleEnd ->
                  broadcaster.broadcastBillingCycleEnd () |> ignore

                  SaveSnapshot account <@> DeleteMessages Int64.MaxValue
            // Event replay on actor start
            | :? AccountEvent as e when mailbox.IsRecovering() ->
               loop <| Some(Account.applyEvent account e)
            | LifecycleEvent _ -> ignored ()
            | :? Akka.Persistence.RecoveryCompleted -> ignored ()
            | :? Akka.Persistence.DeleteMessagesSuccess ->
               if account.Status = AccountStatus.ReadyForDelete then
                  DeleteSnapshots(SnapshotSelectionCriteria Int64.MaxValue)
               else
                  ignored ()
            | :? Akka.Persistence.DeleteMessagesFailure as e ->
               logError $"Failure to delete message history %s{e.Cause.Message}"
               unhandled ()
            | :? PersistentLifecycleEvent as e ->
               match e with
               | ReplaySucceed -> ignored ()
               | ReplayFailed(exn, _) ->
                  failwith $"Persistence replay failed: %s{exn.Message}"
               | PersistRejected(exn, _, _)
               | PersistFailed(exn, _, _) ->
                  broadcaster.broadcastError exn.Message |> ignore
                  failwith $"Persistence failed: %s{exn.Message}"
            | :? SaveSnapshotSuccess -> ignored ()
            | :? SaveSnapshotFailure as e ->
               logError $"SaveSnapshotFailure {e.Metadata}"
               unhandled ()
            | :? DeleteSnapshotsSuccess ->
               if account.Status = AccountStatus.ReadyForDelete then
                  passivate ()
               else
                  ignored ()
            | :? DeleteSnapshotsFailure as e ->
               logError $"DeleteSnapshotsFailure %s{e.Cause.Message}"
               unhandled ()
            | msg ->
               logError $"Unknown message {msg}"
               unhandled ()
      }

      loop None

   propsPersist handler

let get (sys: ActorSystem) (entityId: Guid) =
   AkklingExt.getEntityRef sys "account" entityId

let initProps
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (system: ActorSystem)
   =
   let getOrStartInternalTransferActor mailbox =
      InternalTransferRecipientActor.getOrStart mailbox <| get system

   actorProps
      persistence
      broadcaster
      getOrStartInternalTransferActor
      DomesticTransferRecipientActor.get
      EmailActor.get
      AccountClosureActor.get
      BillingCycleActor.start
      {
         createUser = Bank.User.Api.createUser
      }

[<RequireQualifiedAccess>]
module AccountActor

open System
open Akka.Actor
open Akka.Persistence
open Akkling
open Akkling.Persistence
open Akkling.Cluster.Sharding

open Lib.Types
open Lib.ActivePatterns
open BankTypes
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.User.Api

let private persist e =
   e |> Event |> box |> Persist :> Effect<_>

let start
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (system: ActorSystem)
   =
   let actorName = ActorMetadata.account.Name

   let handler (mailbox: Eventsourced<obj>) =
      let logError = logError mailbox
      let logWarning = logWarning mailbox

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
               | TransferPending e -> mailbox.Self <! DispatchTransfer e
               | TransferDeposited e ->
                  EmailActor.get system
                  <! EmailActor.TransferDeposited(e, newState)
               | CreatedAccount _ ->
                  EmailActor.get system <! EmailActor.AccountOpen newState
               | AccountClosed _ ->
                  AccountClosureActor.get system
                  <! AccountClosureActor.Register newState
               | _ -> ()

               loop (Some newState)
            | :? SnapshotOffer as o -> loop <| Some(unbox o.Snapshot)
            | :? AccountMessage as msg ->
               match msg with
               | InitAccount cmd ->
                  let evt = cmd |> CreatedAccountEvent.create
                  // TODO: Consider creating a user actor & integrating an
                  //       auth workflow in the future.
                  //       Create the record & move along for now.
                  let (user: User.User) = {
                     FirstName = evt.Data.FirstName
                     LastName = evt.Data.LastName
                     AccountId = evt.EntityId
                     Email = evt.Data.Email
                  }

                  createUser(user).Wait()

                  mailbox.Self <! UserCreated evt
                  ignored ()
               | Lookup ->
                  mailbox.Sender() <! accountOpt
                  ignored ()
               | BillingCycle _ when accountOpt.IsSome ->
                  ignored <| BillingCycleActor.start mailbox persistence account
               | StateChange cmd ->
                  let validation = Account.stateTransition account cmd

                  match validation with
                  | Error err ->
                     broadcaster.broadcastError err |> ignore
                     logWarning $"Validation fail {err}"

                     match err with
                     | Contains "InsufficientBalance"
                     | Contains "ExceededDailyDebit" ->
                        EmailActor.get system
                        <! EmailActor.DebitDeclined(err, account)

                     ignored ()
                  | Ok(event, _) -> persist event
               | DispatchTransfer evt ->
                  match evt.Data.Recipient.AccountEnvironment with
                  | RecipientAccountEnvironment.Internal ->
                     InternalTransferRecipientActor.getOrStart mailbox <! evt
                  | RecipientAccountEnvironment.Domestic ->
                     DomesticTransferRecipientActor.get system
                     <! (evt |> DomesticTransferRecipientActor.TransferPending)
                  | _ -> ()

                  ignored ()
               | UserCreated(evt: BankEvent<CreatedAccount>) ->
                  persist <| CreatedAccount evt
               | Delete ->
                  // TODO: Fix passivate shutting down the actor
                  //       before the DeleteSnapshotsSuccess
                  //       message arrives causing an unnecessary
                  //       dead letter.
                  DeleteSnapshots(SnapshotSelectionCriteria Int64.MaxValue)
                  <@> DeleteMessages Int64.MaxValue
                  <@> passivate ()
               | BillingCycleEnd ->
                  broadcaster.broadcastBillingCycleEnd () |> ignore

                  SaveSnapshot account :> Effect<_>
                  <@> DeleteMessages Int64.MaxValue
            // Event replay on actor start
            | :? AccountEvent as e when mailbox.IsRecovering() ->
               loop <| Some(Account.applyEvent account e)
            | LifecycleEvent _ -> ignored ()
            | :? Akka.Persistence.RecoveryCompleted -> ignored ()
            | :? Akka.Persistence.DeleteMessagesSuccess -> ignored ()
            | :? Akka.Persistence.DeleteMessagesFailure as e ->
               logError $"Failure to delete message history {e.Cause.Message}"
               unhandled ()
            | :? PersistentLifecycleEvent as e ->
               match e with
               | ReplaySucceed -> ignored ()
               | ReplayFailed(exn, _) ->
                  failwith $"Persistence replay failed: {exn.Message}"
               | PersistRejected(exn, _, _)
               | PersistFailed(exn, _, _) ->
                  broadcaster.broadcastError exn.Message |> ignore
                  failwith $"Persistence failed: {exn.Message}"
            | :? SaveSnapshotSuccess -> ignored ()
            | :? SaveSnapshotFailure as e ->
               logError $"SaveSnapshotFailure {e.Metadata}"
               unhandled ()
            | msg ->
               logError $"Unknown message {msg}"
               unhandled ()
      }

      loop None

   AkklingExt.entityFactoryFor system actorName <| propsPersist handler

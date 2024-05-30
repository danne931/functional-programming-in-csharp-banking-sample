[<RequireQualifiedAccess>]
module AccountActor

open System
open Akka.Actor
open Akka.Persistence
open Akka.Persistence.Extras
open Akka.Streams
open Akkling
open Akkling.Persistence
open Akkling.Cluster.Sharding

open Lib.SharedTypes
open Lib.Types
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open BillingStatement

let private dispatchTransfer
   (getOrStartInternalTransferActor:
      Actor<_> -> IActorRef<InternalTransferMessage>)
   (getDomesticTransferActor: ActorSystem -> IActorRef<DomesticTransferMessage>)
   mailbox
   evt
   =
   let txn = TransferEventToTransaction.fromPending evt

   match evt.Data.Recipient.AccountEnvironment with
   | RecipientAccountEnvironment.Internal ->
      getOrStartInternalTransferActor mailbox
      <! InternalTransferMessage.TransferRequest txn
   | RecipientAccountEnvironment.Domestic ->
      let msg =
         DomesticTransferMessage.TransferRequest(
            TransferServiceAction.TransferRequest,
            txn
         )

      getDomesticTransferActor mailbox.System <! msg

// Pass monthly billing statement to BillingStatementActor.
// Conditionally apply monthly maintenance fee.
// Email account owner to notify of billing statement availability.
let private billingCycle
   (getBillingStatementActor: ActorSystem -> IActorRef<BillingStatementMessage>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   (mailbox: Eventsourced<obj>)
   account
   =
   let billing =
      BillingStatement.billingStatement account <| mailbox.LastSequenceNr()

   getBillingStatementActor mailbox.System <! RegisterBillingStatement billing

   let criteria = account.MaintenanceFeeCriteria

   if criteria.CanSkipFee then
      let msg =
         SkipMaintenanceFeeCommand.create account.CompositeId {
            Reason = criteria
         }
         |> AccountCommand.SkipMaintenanceFee
         |> AccountMessage.StateChange

      mailbox.Parent() <! msg
   else
      let msg =
         MaintenanceFeeCommand.create account.CompositeId
         |> AccountCommand.MaintenanceFee
         |> AccountMessage.StateChange

      mailbox.Parent() <! msg

   getEmailActor mailbox.System <! EmailActor.BillingStatement account

let actorProps
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (getOrStartInternalTransferActor:
      Actor<_> -> IActorRef<InternalTransferMessage>)
   (getDomesticTransferActor: ActorSystem -> IActorRef<DomesticTransferMessage>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   (getAccountClosureActor: ActorSystem -> IActorRef<AccountClosureMessage>)
   (getBillingStatementActor: ActorSystem -> IActorRef<BillingStatementMessage>)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logWarning, logError = logWarning mailbox, logError mailbox

      let rec loop (accountOpt: Account option) = actor {
         let! msg = mailbox.Receive()
         let account = Option.defaultValue Account.empty accountOpt

         match box msg with
         | Persisted mailbox e ->
            let (AccountMessage.Event evt) = unbox e
            let newState = Account.applyEvent account evt
            broadcaster.accountEventPersisted evt newState |> ignore

            match evt with
            | InternalTransferRecipient e ->
               let sender = {
                  Name = newState.Name
                  OrgId = newState.OrgId
                  AccountId = newState.EntityId
               }

               getOrStartInternalTransferActor mailbox
               <! InternalTransferMessage.ConfirmRecipient(
                  sender,
                  e.Data.toRecipient ()
               )
            | TransferPending e ->
               dispatchTransfer
                  getOrStartInternalTransferActor
                  getDomesticTransferActor
                  mailbox
                  e
            | TransferDeposited e ->
               getEmailActor mailbox.System
               <! EmailActor.TransferDeposited(e, newState)
            | CreatedAccount _ ->
               getEmailActor mailbox.System <! EmailActor.AccountOpen newState
            | AccountEvent.AccountClosed _ ->
               getAccountClosureActor mailbox.System
               <! AccountClosureMessage.Register newState
            | BillingCycleStarted _ ->
               billingCycle
                  getBillingStatementActor
                  getEmailActor
                  mailbox
                  account
            | _ -> ()

            return! loop <| Some newState
         | :? SnapshotOffer as o -> return! loop <| Some(unbox o.Snapshot)
         | :? ConfirmableMessageEnvelope as envelope ->
            match envelope.Message with
            | :? AccountMessage as msg ->
               match msg with
               | StateChange cmd ->
                  let validation = Account.stateTransition account cmd

                  match validation with
                  | Ok(event, _) ->
                     return!
                        confirmPersist
                           mailbox
                           (AccountMessage.Event event)
                           envelope.ConfirmationId
                  | Error err ->
                     logWarning $"Validation fail %s{string err}"

                     let signalRBroadcastValidationErr () =
                        broadcaster.accountEventValidationFail
                           account.EntityId
                           err

                     match err with
                     | StateTransitionError e ->
                        match e with
                        // NOOP
                        | TransferProgressNoChange
                        | TransferAlreadyProgressedToApprovedOrRejected
                        | AccountNotReadyToActivate -> ()
                        // Send email for declined debit.
                        // Broadcast validation errors to UI.
                        | InsufficientBalance _
                        | ExceededDailyDebit _ ->
                           getEmailActor mailbox.System
                           <! EmailActor.DebitDeclined(string err, account)

                           signalRBroadcastValidationErr ()
                        | _ -> signalRBroadcastValidationErr ()
                     | _ -> ()
               | msg ->
                  logError
                     $"Unknown message in ConfirmableMessageEnvelope - {msg}"

                  unhandled ()
            | msg ->
               logError $"Unknown message in ConfirmableMessageEnvelope - {msg}"
               return unhandled ()
         | :? AccountMessage as msg ->
            match msg with
            | GetAccount -> mailbox.Sender() <! accountOpt
            | GetEvents ->
               match accountOpt with
               | None -> mailbox.Sender() <! []
               | Some account ->
                  mailbox.Sender() <!| persistence.getEvents account.EntityId
            | Delete ->
               let newState = {
                  account with
                     Status = AccountStatus.ReadyForDelete
               }

               return! loop (Some newState) <@> DeleteMessages Int64.MaxValue
         // Event replay on actor start
         | :? AccountEvent as e when mailbox.IsRecovering() ->
            return! loop <| Some(Account.applyEvent account e)
         | msg ->
            PersistentActorEventHandler.handleEvent
               {
                  PersistentActorEventHandler.init with
                     DeleteMessagesSuccess =
                        fun _ ->
                           if account.Status = AccountStatus.ReadyForDelete then
                              logDebug mailbox "<Passivate>"
                              passivate ()
                           else
                              ignored ()
                     PersistFailed =
                        fun _ err evt sequenceNr ->
                           broadcaster.accountEventPersistenceFail
                              account.EntityId
                              (Err.DatabaseError err)
                           |> ignore

                           ignored ()
               }
               mailbox
               msg
      }

      loop None

   propsPersist handler

let get (sys: ActorSystem) (entityId: Guid) : IEntityRef<AccountMessage> =
   getEntityRef sys ClusterMetadata.accountShardRegion entityId

let private getAccountEvents
   (actorSystem: ActorSystem)
   (id: Guid)
   : AccountEvent list Async
   =
   ActorUtil
      .readJournal(actorSystem)
      .CurrentEventsByPersistenceId(string id, 0, Int64.MaxValue)
      .RunAggregate(
         [],
         (fun acc envelope -> unbox envelope.Event :: acc),
         actorSystem.Materializer()
      )
   |> Async.AwaitTask

let isPersistableMessage (msg: obj) =
   match msg with
   | :? AccountMessage as msg ->
      match msg with
      | AccountMessage.StateChange _ -> true
      | _ -> false
   | _ -> false

let initProps
   (broadcaster: AccountBroadcast)
   (system: ActorSystem)
   (supervisorOpts: PersistenceSupervisorOptions)
   (persistenceId: string)
   =
   let getOrStartInternalTransferActor mailbox =
      InternalTransferRecipientActor.getOrStart mailbox <| get system

   let childProps =
      actorProps
         { getEvents = getAccountEvents system }
         broadcaster
         getOrStartInternalTransferActor
         DomesticTransferRecipientActor.get
         EmailActor.get
         AccountClosureActor.get
         BillingStatementActor.get

   persistenceSupervisor
      supervisorOpts
      isPersistableMessage
      childProps
      persistenceId

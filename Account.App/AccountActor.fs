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
open FsToolkit.ErrorHandling

open Lib.Types
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open BillingStatement

let actorProps
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (getOrStartInternalTransferActor: Actor<_> -> IActorRef<TransferTransaction>)
   (getDomesticTransferActor: ActorSystem -> IActorRef<DomesticTransferMessage>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   (getAccountClosureActor: ActorSystem -> IActorRef<AccountClosureMessage>)
   (getBillingStatementActor: ActorSystem -> IActorRef<BillingStatementMessage>)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logWarning, logError = logWarning mailbox, logError mailbox

      let rec loop (accountOpt: AccountState option) = actor {
         let! msg = mailbox.Receive()
         let account = Option.defaultValue AccountState.empty accountOpt

         match box msg with
         | Persisted mailbox e ->
            let (AccountMessage.Event evt) = unbox e
            let newState = Account.applyEvent account evt
            broadcaster.accountEventPersisted evt newState |> ignore

            match evt with
            | TransferPending e -> mailbox.Self <! DispatchTransfer e
            | TransferDeposited e ->
               getEmailActor mailbox.System
               <! EmailActor.TransferDeposited(e, newState)
            | CreatedAccount _ ->
               getEmailActor mailbox.System <! EmailActor.AccountOpen newState
            | AccountClosed _ ->
               getAccountClosureActor mailbox.System
               <! AccountClosureMessage.Register newState
            | _ -> ()

            return! loop <| Some newState
         | :? SnapshotOffer as o -> return! loop <| Some(unbox o.Snapshot)
         | :? ConfirmableMessageEnvelope as envelope ->
            match envelope.Message with
            | :? AccountMessage as msg ->
               match msg with
               | StateChange(CreateAccount cmd) when accountOpt.IsSome ->
                  logWarning
                     $"Account already exists - ignore create account command {cmd.EntityId}"

                  return ignored ()
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
                     let errMsg = string err
                     logWarning $"Validation fail %s{errMsg}"

                     let signalRBroadcastValidationErr () =
                        broadcaster.accountEventValidationFail
                           account.EntityId
                           errMsg

                     match err with
                     | StateTransitionError e ->
                        match e with
                        | InsufficientBalance _
                        | ExceededDailyDebit _ ->
                           signalRBroadcastValidationErr ()

                           getEmailActor mailbox.System
                           <! EmailActor.DebitDeclined(errMsg, account)
                        | TransferProgressNoChange
                        | TransferAlreadyProgressedToApprovedOrRejected -> ()
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
            | DispatchTransfer evt ->
               let txn = TransferEventToTransaction.fromPending evt

               match evt.Data.Recipient.AccountEnvironment with
               | RecipientAccountEnvironment.Internal ->
                  getOrStartInternalTransferActor mailbox <! txn
               | RecipientAccountEnvironment.Domestic ->
                  let msg =
                     DomesticTransferMessage.TransferRequest(
                        TransferServiceAction.TransferRequest,
                        txn
                     )

                  getDomesticTransferActor mailbox.System <! msg
               | _ -> ()
            | Delete ->
               let newState = {
                  account with
                     Status = AccountStatus.ReadyForDelete
               }

               return! loop (Some newState) <@> DeleteMessages Int64.MaxValue
            | BillingCycle when
               accountOpt.IsSome && account.CanProcessTransactions
               ->
               let billing =
                  BillingStatement.billingStatement account
                  <| mailbox.LastSequenceNr()

               getBillingStatementActor mailbox.System
               <! RegisterBillingStatement billing

               // Maintenance fee conditionally applied after account transactions
               // have been consolidated. If applied, it will be the first transaction
               // of the new billing cycle.
               let criteria = account.MaintenanceFeeCriteria
               let accountId = account.EntityId

               if criteria.CanSkipFee then
                  let msg =
                     SkipMaintenanceFeeCommand(accountId, criteria)
                     |> (StateChange << SkipMaintenanceFee)

                  mailbox.Parent() <! msg
               else
                  let msg =
                     MaintenanceFeeCommand accountId
                     |> (StateChange << MaintenanceFee)

                  mailbox.Parent() <! msg

               getEmailActor mailbox.System
               <! EmailActor.BillingStatement account

               return! loop <| Some { account with Events = [] }
            | BillingCycle ->
               logWarning
                  "Account not able to process txns. Ignore billing cycle."

               return ignored ()
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
                              err.Message
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

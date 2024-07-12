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
open Bank.Employee.Domain
open BillingStatement
open DomesticTransferRecipientActor

type private InternalTransferMsg =
   InternalTransferRecipientActor.InternalTransferMessage

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

// TODO: Comment out all account related email messages until
//       I associate account owners with the account.
//getEmailActor mailbox.System <! EmailActor.BillingStatement account

let actorProps
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (getOrStartInternalTransferActor: Actor<_> -> IActorRef<InternalTransferMsg>)
   (getDomesticTransferActor: ActorSystem -> IActorRef<DomesticTransferMessage>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   (getAccountClosureActor: ActorSystem -> IActorRef<AccountClosureMessage>)
   (getBillingStatementActor: ActorSystem -> IActorRef<BillingStatementMessage>)
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logWarning, logError = logWarning mailbox, logError mailbox

      let rec loop (accountOpt: Account option) = actor {
         let! msg = mailbox.Receive()
         let account = Option.defaultValue Account.empty accountOpt

         match box msg with
         | Persisted mailbox e ->
            let (AccountMessage.Event evt) = unbox e
            let account = Account.applyEvent account evt

            broadcaster.accountEventPersisted evt account

            match evt with
            | DebitedAccount e ->
               let info = e.Data
               let employee = info.EmployeePurchaseReference

               let msg =
                  ApproveDebitCommand.create (employee.EmployeeId, e.OrgId) {
                     Info = {
                        AccountId = account.AccountId
                        CorrelationId = e.CorrelationId
                        EmployeeId = employee.EmployeeId
                        CardId = employee.CardId
                        CardNumberLast4 = employee.EmployeeCardNumberLast4
                        Date = info.Date
                        Amount = info.Amount
                        Origin = info.Origin
                        Reference = info.Reference
                     }
                  }
                  |> EmployeeCommand.ApproveDebit
                  |> EmployeeMessage.StateChange

               getEmployeeRef employee.EmployeeId <! msg
            | InternalTransferRecipient e ->
               let msg =
                  InternalTransferMsg.ConfirmRecipient {
                     Sender = {
                        Name = account.Name
                        OrgId = account.OrgId
                        AccountId = account.AccountId
                     }
                     Recipient = e.Data.Recipient
                     InitiatedBy = e.InitiatedById
                  }

               getOrStartInternalTransferActor mailbox <! msg
            | EditedDomesticTransferRecipient e ->
               // Retry failed domestic transfers if they were previously
               // declined due to invalid account info.
               let recipientId = e.Data.Recipient.AccountId

               let invalidAccount =
                  TransferDeclinedReason.InvalidAccountInfo
                  |> DomesticTransferProgress.Failed

               account.FailedDomesticTransfers
               |> Map.filter (fun _ transfer ->
                  transfer.Recipient.AccountId = recipientId
                  && transfer.Status = invalidAccount)
               |> Map.iter (fun _ transfer ->
                  let cmd =
                     DomesticTransferToCommand.retry transfer
                     |> AccountCommand.DomesticTransfer

                  mailbox.Parent() <! AccountMessage.StateChange cmd)
            | InternalTransferPending e ->
               getOrStartInternalTransferActor mailbox
               <! InternalTransferMsg.TransferRequest e
            | DomesticTransferPending e ->
               let txn = TransferEventToDomesticTransfer.fromPending e

               let msg =
                  DomesticTransferMessage.TransferRequest(
                     DomesticTransferServiceAction.TransferRequest,
                     txn
                  )

               getDomesticTransferActor mailbox.System <! msg
            | TransferDeposited e -> ()
            (*
               getEmailActor mailbox.System
               <! EmailActor.TransferDeposited(e, account)
               *)
            | CreatedAccount _ -> ()
            //getEmailActor mailbox.System <! EmailActor.AccountOpen account
            | AccountEvent.AccountClosed e ->
               getAccountClosureActor mailbox.System
               <! AccountClosureMessage.Register(account, e.InitiatedById)
            | BillingCycleStarted _ ->
               billingCycle
                  getBillingStatementActor
                  getEmailActor
                  mailbox
                  account
            | _ -> ()

            return! loop <| Some account
         | :? SnapshotOffer as o -> return! loop <| Some(unbox o.Snapshot)
         | :? ConfirmableMessageEnvelope as envelope ->
            match envelope.Message with
            | :? AccountMessage as msg ->
               match msg with
               | AccountMessage.StateChange cmd ->
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
                           account.AccountId
                           err

                     match err with
                     | AccountStateTransitionError e ->
                        match e with
                        // NOOP
                        | TransferProgressNoChange
                        | TransferAlreadyProgressedToApprovedOrRejected
                        | AccountNotReadyToActivate ->
                           logDebug mailbox $"AccountTransferActor NOOP msg {e}"
                        | InsufficientBalance e ->
                           match cmd with
                           | AccountCommand.Debit cmd ->
                              let info = cmd.Data
                              let employee = cmd.Data.EmployeePurchaseReference

                              let msg =
                                 DeclineDebitCommand.create
                                    (employee.EmployeeId, cmd.OrgId)
                                    {
                                       Info = {
                                          AccountId = account.AccountId
                                          CorrelationId = cmd.CorrelationId
                                          EmployeeId = employee.EmployeeId
                                          CardId = employee.CardId
                                          CardNumberLast4 =
                                             employee.EmployeeCardNumberLast4
                                          Date = info.Date
                                          Amount = info.Amount
                                          Origin = info.Origin
                                          Reference = info.Reference
                                       }
                                    }
                                 |> EmployeeCommand.DeclineDebit
                                 |> EmployeeMessage.StateChange

                              getEmployeeRef employee.EmployeeId <! msg
                           | _ -> ()
                           (*
                           getEmailActor mailbox.System
                           <! EmailActor.DebitDeclined(string err, account)
                           *)

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
            | AccountMessage.GetAccount -> mailbox.Sender() <! accountOpt
            | AccountMessage.GetEvents ->
               match accountOpt with
               | None -> mailbox.Sender() <! []
               | Some account ->
                  mailbox.Sender() <!| persistence.getEvents account.AccountId
            | AccountMessage.Delete ->
               let account = {
                  account with
                     Status = AccountStatus.ReadyForDelete
               }

               return! loop (Some account) <@> DeleteMessages Int64.MaxValue
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
                              account.AccountId
                              (Err.DatabaseError err)

                           ignored ()
               }
               mailbox
               msg
      }

      loop None

   propsPersist handler

let get (sys: ActorSystem) (accountId: AccountId) : IEntityRef<AccountMessage> =
   getEntityRef sys ClusterMetadata.accountShardRegion (AccountId.get accountId)

let private getAccountEvents
   (actorSystem: ActorSystem)
   (id: AccountId)
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
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
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
         getEmployeeRef

   persistenceSupervisor
      supervisorOpts
      isPersistableMessage
      childProps
      persistenceId

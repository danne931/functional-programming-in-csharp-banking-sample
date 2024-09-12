[<RequireQualifiedAccess>]
module AccountActor

open System
open Akka.Actor
open Akka.Persistence
open Akka.Persistence.Extras
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
   (state: AccountWithEvents)
   (evt: BankEvent<BillingCycleStarted>)
   =
   let account = state.Info

   let billingPeriod = {
      Month = evt.Data.Month
      Year = evt.Data.Year
   }

   let billing =
      BillingStatement.billingStatement state billingPeriod
      <| mailbox.LastSequenceNr()

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
   (broadcaster: AccountBroadcast)
   (getOrStartInternalTransferActor: Actor<_> -> IActorRef<InternalTransferMsg>)
   (getDomesticTransferActor: ActorSystem -> IActorRef<DomesticTransferMessage>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   (getAccountClosureActor: ActorSystem -> IActorRef<AccountClosureMessage>)
   (getBillingStatementActor: ActorSystem -> IActorRef<BillingStatementMessage>)
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logWarning, logError = logWarning mailbox, logError mailbox

      let rec loop (stateOpt: AccountWithEvents option) = actor {
         let! msg = mailbox.Receive()

         let state =
            stateOpt
            |> Option.defaultValue { Info = Account.empty; Events = [] }

         let account = state.Info

         match box msg with
         | Persisted mailbox e ->
            let (AccountMessage.Event evt) = unbox e
            let state = Account.applyEvent state evt
            let account = state.Info

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
            | InternalTransferWithinOrgPending e ->
               getOrStartInternalTransferActor mailbox
               <! InternalTransferMsg.TransferRequestWithinOrg e
            | InternalTransferBetweenOrgsPending e ->
               getOrStartInternalTransferActor mailbox
               <! InternalTransferMsg.TransferRequestBetweenOrgs e
            | DomesticTransferPending e ->
               let txn = TransferEventToDomesticTransfer.fromPending e

               let msg =
                  DomesticTransferMessage.TransferRequest(
                     DomesticTransferServiceAction.TransferRequest,
                     txn
                  )

               getDomesticTransferActor mailbox.System <! msg
            | InternalTransferBetweenOrgsDeposited e -> ()
            (*
               getEmailActor mailbox.System
               <! EmailActor.InternalTransferBetweenOrgsDeposited(e, account)
               *)
            | CreatedAccount _ -> ()
            //getEmailActor mailbox.System <! EmailActor.AccountOpen account
            | AccountEvent.AccountClosed e ->
               getAccountClosureActor mailbox.System
               <! AccountClosureMessage.Register account
            | BillingCycleStarted e ->
               billingCycle
                  getBillingStatementActor
                  getEmailActor
                  mailbox
                  state
                  e
            | PlatformPaymentPaid e ->
               let payee = e.Data.BaseInfo.Payee

               let msg =
                  DepositPlatformPaymentCommand.create
                     (payee.AccountId, payee.OrgId)
                     e.CorrelationId
                     e.InitiatedById
                     {
                        BaseInfo = e.Data.BaseInfo
                        PaymentMethod = e.Data.PaymentMethod
                     }
                  |> AccountCommand.DepositPlatformPayment
                  |> AccountMessage.StateChange

               (getAccountRef payee.AccountId) <! msg
            (*
            | ThirdPartyPaymentRequested e ->
               // TODO: Send email requesting payment
            *)
            | _ -> ()

            return! loop <| Some state
         | :? SnapshotOffer as o -> return! loop <| Some(unbox o.Snapshot)
         | :? ConfirmableMessageEnvelope as envelope ->
            match envelope.Message with
            | :? AccountMessage as msg ->
               match msg with
               | AccountMessage.StateChange cmd ->
                  let validation = Account.stateTransition state cmd

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
            | AccountMessage.GetAccount ->
               mailbox.Sender() <! (stateOpt |> Option.map _.Info)
            | AccountMessage.Delete ->
               let state =
                  Some {
                     state with
                        Info.Status = AccountStatus.ReadyForDelete
                  }

               return! loop state <@> DeleteMessages Int64.MaxValue
         // Event replay on actor start
         | :? AccountEvent as e when mailbox.IsRecovering() ->
            return! loop <| Some(Account.applyEvent state e)
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
         broadcaster
         getOrStartInternalTransferActor
         DomesticTransferRecipientActor.get
         EmailActor.get
         AccountClosureActor.get
         BillingStatementActor.get
         getEmployeeRef
         (get system)

   persistenceSupervisor
      supervisorOpts
      isPersistableMessage
      childProps
      persistenceId

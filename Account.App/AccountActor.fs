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
open AutomaticTransfer

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

   let msg =
      EmailActor.EmailMessage.BillingStatement(account.FullName, account.OrgId)

   getEmailActor mailbox.System <! msg

// Account events with an in/out money flow can produce an
// automatic transfer.  Automated transfer account events have
// money flow but they can not generate an auto transfer.
let canProduceAutoTransfer =
   function
   | AccountEvent.InternalAutomatedTransferPending _
   | AccountEvent.InternalAutomatedTransferApproved _
   | AccountEvent.InternalAutomatedTransferRejected _
   | AccountEvent.InternalAutomatedTransferDeposited _ -> false
   | e ->
      let _, flow, _ = AccountEvent.moneyTransaction e
      flow.IsSome

let handleValidationError
   (broadcaster: AccountBroadcast)
   mailbox
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   (account: Account)
   (cmd: AccountCommand)
   (err: Err)
   =
   logWarning
      mailbox
      $"Validation fail %s{string err} for command %s{cmd.GetType().Name}"

   let signalRBroadcastValidationErr () =
      broadcaster.accountEventValidationFail account.AccountId err

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
               DeclineDebitCommand.create (employee.EmployeeId, cmd.OrgId) {
                  Reason =
                     PurchaseDeclinedReason.InsufficientAccountFunds(
                        account.Balance,
                        account.FullName
                     )
                  Info = {
                     AccountId = account.AccountId
                     CorrelationId = cmd.CorrelationId
                     EmployeeId = employee.EmployeeId
                     CardId = employee.CardId
                     CardNumberLast4 = employee.EmployeeCardNumberLast4
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

         signalRBroadcastValidationErr ()
      | _ -> signalRBroadcastValidationErr ()
   | _ -> ()

let actorProps
   (broadcaster: AccountBroadcast)
   (getOrStartInternalTransferActor: Actor<_> -> IActorRef<InternalTransferMsg>)
   (getDomesticTransferActor: ActorSystem -> IActorRef<DomesticTransferMessage>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   (getAccountClosureActor: ActorSystem -> IActorRef<AccountClosureMessage>)
   (getBillingStatementActor: ActorSystem -> IActorRef<BillingStatementMessage>)
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (schedulingRef: IActorRef<SchedulingActor.Message>)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logError = logError mailbox

      let rec loop (stateOpt: AccountWithEvents option) = actor {
         let! msg = mailbox.Receive()

         let state =
            stateOpt
            |> Option.defaultValue { Info = Account.empty; Events = [] }

         let account = state.Info

         let handleValidationError =
            handleValidationError broadcaster mailbox getEmployeeRef account

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
                  DomesticTransferDeclinedReason.InvalidAccountInfo
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
            | InternalAutomatedTransferPending e ->
               getOrStartInternalTransferActor mailbox
               <! InternalTransferMsg.AutomatedTransferRequest e
            | InternalTransferBetweenOrgsScheduled e ->
               schedulingRef
               <! SchedulingActor.Message.ScheduleInternalTransferBetweenOrgs
                     e.Data
            | DomesticTransferScheduled e ->
               schedulingRef
               <! SchedulingActor.Message.ScheduleDomesticTransfer e.Data
            | DomesticTransferPending e ->
               let txn = TransferEventToDomesticTransfer.fromPending e

               let msg =
                  DomesticTransferMessage.TransferRequest(
                     DomesticTransferServiceAction.TransferRequest,
                     txn
                  )

               getDomesticTransferActor mailbox.System <! msg
            | InternalTransferBetweenOrgsDeposited e ->
               let msg =
                  EmailActor.EmailMessage.InternalTransferBetweenOrgsDeposited(
                     {
                        OrgId = account.OrgId
                        AccountName = account.FullName
                        Amount = e.Data.BaseInfo.Amount
                        SenderBusinessName = e.Data.BaseInfo.Sender.Name
                     }
                  )

               getEmailActor mailbox.System <! msg
            | CreatedAccount e ->
               let msg =
                  EmailActor.EmailMessage.AccountOpen(
                     account.FullName,
                     account.OrgId
                  )

               getEmailActor mailbox.System <! msg
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

            if
               canProduceAutoTransfer evt
               && not account.AutoTransfersPerTransaction.IsEmpty
            then
               mailbox.Self
               <! AccountMessage.AutoTransferCompute Frequency.PerTransaction

            return! loop <| Some state
         | :? SnapshotOffer as o -> return! loop <| Some(unbox o.Snapshot)
         | :? ConfirmableMessageEnvelope as envelope ->
            let unknownMsg msg =
               logError $"Unknown message in ConfirmableMessageEnvelope - {msg}"
               unhandled ()

            match envelope.Message with
            | :? AccountMessage as msg ->
               match msg with
               | AccountMessage.StateChange cmd ->
                  let validation = Account.stateTransition state cmd

                  match validation with
                  | Ok(evt, _) ->
                     return!
                        confirmPersist
                           mailbox
                           (AccountMessage.Event evt)
                           envelope.ConfirmationId
                  | Error err -> handleValidationError cmd err
               | msg -> return unknownMsg msg
            | msg -> return unknownMsg msg
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
            | AccountMessage.AutoTransferCompute frequency ->
               let transfers =
                  match frequency with
                  | Frequency.PerTransaction ->
                     account.AutoTransfersPerTransaction
                  | Frequency.Schedule CronSchedule.Daily ->
                     account.AutoTransfersDaily
                  | Frequency.Schedule CronSchedule.TwiceMonthly ->
                     account.AutoTransfersTwiceMonthly

               let transfersOut, transfersIn =
                  transfers
                  |> List.partition (fun t ->
                     t.Transfer.Sender.AccountId = account.AccountId)

               // NOTE: Transfers-in
               // Computed transfers which are generated from a
               // TargetBalanceRule.  When the target balance is lower
               // than desired, the ManagingPartnerAccount is designated
               // as the sender in order to restore funds to the target.
               for t in transfersIn do
                  let msg =
                     InternalAutoTransferCommand.create t
                     |> AccountCommand.InternalAutoTransfer
                     |> AccountMessage.StateChange

                  (getAccountRef t.Transfer.Sender.AccountId) <! msg

               // NOTE: Transfers-out
               // Outgoing auto transfers are computed, applied against the
               // aggregate state, and persisted in one go.
               //
               // If instead they were computed and then sent as individual
               // StateChange InternalAutoTransfer messages then you would
               // run the risk of other StateChange messages being processed
               // before the computed InternalAutoTransfer message leading to
               // InsufficientBalance validation errors in busy workloads.
               match transfersOut with
               | [] -> return ignored ()
               | transfers ->
                  let validations =
                     transfers
                     |> List.map (
                        InternalAutoTransferCommand.create
                        >> AccountCommand.InternalAutoTransfer
                     )
                     |> List.fold
                           (fun acc cmd ->
                              match acc with
                              | Ok(accountState, events) ->
                                 Account.stateTransition accountState cmd
                                 |> Result.map (fun (evt, newState) ->
                                    newState, evt :: events)
                                 |> Result.mapError (fun err -> cmd, err)
                              | Error err -> Error err)
                           (Ok(state, []))

                  match validations with
                  | Ok(_, evts) ->
                     let evts = List.map (AccountMessage.Event >> box) evts
                     return! PersistAll evts
                  | Error(cmd, err) -> handleValidationError cmd err
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
         (SchedulingActor.get system)

   persistenceSupervisor
      supervisorOpts
      isPersistableMessage
      childProps
      persistenceId

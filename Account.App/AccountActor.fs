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
open BillingStatement

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
   (getAccountClosureActor: ActorSystem -> IActorRef<AccountClosureMessage>)
   (getBillingStatementActor: ActorSystem -> IActorRef<BillingStatementMessage>)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logWarning = logWarning mailbox

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
               | None -> mailbox.Sender() <! []
               | Some account ->
                  mailbox.Sender() <!| persistence.getEvents account.EntityId
            | StateChange(CreateAccount cmd) when accountOpt.IsSome ->
               logWarning
                  $"Account already exists - ignore create account command {cmd.EntityId}"

               return ignored ()
            | StateChange cmd ->
               let validation = Account.stateTransition account cmd

               match validation with
               | Ok(event, _) -> return! persist event
               | Error err ->
                  let errMsg = string err

                  broadcaster.accountEventValidationFail account.EntityId errMsg
                  |> ignore

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
            | DispatchTransfer evt ->
               match evt.Data.Recipient.AccountEnvironment with
               | RecipientAccountEnvironment.Internal ->
                  getOrStartInternalTransferActor mailbox <! evt
               | RecipientAccountEnvironment.Domestic ->
                  getDomesticTransferActor mailbox.System
                  <! (evt |> DomesticTransferRecipientActor.TransferPending)
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

                  mailbox.Self <! msg
               else
                  let msg =
                     MaintenanceFeeCommand accountId
                     |> (StateChange << MaintenanceFee)

                  mailbox.Self <! msg

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

let initProps (broadcaster: AccountBroadcast) (system: ActorSystem) =
   let getOrStartInternalTransferActor mailbox =
      InternalTransferRecipientActor.getOrStart mailbox <| get system

   actorProps
      { getEvents = getAccountEvents system }
      broadcaster
      getOrStartInternalTransferActor
      DomesticTransferRecipientActor.get
      EmailActor.get
      AccountClosureActor.get
      BillingStatementActor.get

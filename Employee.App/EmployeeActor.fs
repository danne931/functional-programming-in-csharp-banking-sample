[<RequireQualifiedAccess>]
module EmployeeActor

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
open Bank.Employee.Domain

let actorProps
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   (getEvents: EmployeeId -> EmployeeEvent list Async)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logWarning, logError = logWarning mailbox, logError mailbox

      let rec loop (employeeOpt: Employee option) = actor {
         let! msg = mailbox.Receive()
         let employee = Option.defaultValue Employee.empty employeeOpt

         match box msg with
         | Persisted mailbox e ->
            let (EmployeeMessage.Event evt) = unbox e
            let employee = Employee.applyEvent employee evt

            match evt with
            | DebitRequested e ->
               let accountId = e.Data.Info.AccountId
               let info = e.Data.Info

               let cmd =
                  DebitCommand.create (accountId, e.OrgId) e.CorrelationId {
                     Date = info.Date
                     Amount = info.Amount
                     Origin = info.Origin
                     Reference = info.Reference
                     EmployeePurchaseReference = {
                        EmployeeName = employee.Name
                        EmployeeCardNumberLast4 =
                           employee.Cards[info.CardId]
                              .SecurityInfo.CardNumber.Last4
                        EmployeeId = info.EmployeeId
                        CardId = info.CardId
                     }
                  }

               // Notify associated company account actor of
               // debit request and wait for approval before
               // sending a response to issuing card network.
               getAccountRef accountId
               <! AccountMessage.StateChange(AccountCommand.Debit cmd)
            | DebitApproved e ->
               // Notify card network which issued the debit request
               // to our bank.
               ()
            | DebitDeclined e ->
               // Notify card network which issued the debit request
               // to our bank.
               ()
            | _ -> ()

            return! loop <| Some employee
         | :? SnapshotOffer as o -> return! loop <| Some(unbox o.Snapshot)
         | :? ConfirmableMessageEnvelope as envelope ->
            match envelope.Message with
            | :? EmployeeMessage as msg ->
               match msg with
               | StateChange cmd ->
                  let validation = Employee.stateTransition employee cmd

                  match validation with
                  | Ok(evt, _) ->
                     return!
                        confirmPersist
                           mailbox
                           (EmployeeMessage.Event evt)
                           envelope.ConfirmationId
                  | Error err ->
                     logWarning $"Validation fail %s{string err}"

                     match err with
                     | EmployeeStateTransitionError e ->
                        match e with
                        // Noop
                        | DebitAlreadyProgressedToApprovedOrDeclined -> ()
                        | ExceededDailyDebit(limit, accrued) ->
                           let msg =
                              EmailActor.DebitDeclinedExceededDailyDebit(
                                 limit,
                                 accrued,
                                 employee.Email
                              )

                           getEmailActor mailbox.System <! msg
                        | _ -> ()
                     | _ -> ()
               | msg ->
                  logError
                     $"Unknown message in ConfirmableMessageEnvelope - {msg}"

                  unhandled ()
            | msg ->
               logError $"Unknown message in ConfirmableMessageEnvelope - {msg}"
               return unhandled ()
         | :? EmployeeMessage as msg ->
            match msg with
            | GetEmployee -> mailbox.Sender() <! employeeOpt
            | GetEvents ->
               match employeeOpt with
               | None -> mailbox.Sender() <! []
               | Some employee ->
                  mailbox.Sender() <!| getEvents employee.EmployeeId
            | Delete ->
               let newState = {
                  employee with
                     Status = EmployeeStatus.ReadyForDelete
               }

               return! loop (Some newState) <@> DeleteMessages Int64.MaxValue
         // Event replay on actor start
         | :? EmployeeEvent as e when mailbox.IsRecovering() ->
            return! loop <| Some(Employee.applyEvent employee e)
         | msg ->
            PersistentActorEventHandler.handleEvent
               {
                  PersistentActorEventHandler.init with
                     DeleteMessagesSuccess =
                        fun _ ->
                           if
                              employee.Status = EmployeeStatus.ReadyForDelete
                           then
                              logDebug mailbox "<Passivate Employee Actor>"
                              passivate ()
                           else
                              ignored ()
               }
               mailbox
               msg
      }

      loop None

   propsPersist handler

let get
   (sys: ActorSystem)
   (employeeId: EmployeeId)
   : IEntityRef<EmployeeMessage>
   =
   getEntityRef
      sys
      ClusterMetadata.employeeShardRegion
      (EmployeeId.get employeeId)

let private getEmployeeEvents
   (actorSystem: ActorSystem)
   (id: EmployeeId)
   : EmployeeEvent list Async
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
   | :? EmployeeMessage as msg ->
      match msg with
      | EmployeeMessage.StateChange _ -> true
      | _ -> false
   | _ -> false

let initProps
   (system: ActorSystem)
   (supervisorOpts: PersistenceSupervisorOptions)
   (persistenceId: string)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   =

   let childProps =
      actorProps getAccountRef EmailActor.get (getEmployeeEvents system)

   persistenceSupervisor
      supervisorOpts
      isPersistableMessage
      childProps
      persistenceId

[<RequireQualifiedAccess>]
module EmployeeActor

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
open Bank.Employee.Domain

let actorProps
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (getEmailActor: ActorSystem -> IActorRef<EmailActor.EmailMessage>)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logWarning, logError = logWarning mailbox, logError mailbox

      let rec loop (employeeOpt: EmployeeWithEvents option) = actor {
         let! msg = mailbox.Receive()

         let state =
            employeeOpt
            |> Option.defaultValue { Info = Employee.empty; Events = [] }

         let employee = state.Info

         match box msg with
         | Persisted mailbox e ->
            let (EmployeeMessage.Event evt) = unbox e
            let state = Employee.applyEvent state evt
            let employee = state.Info

            match evt with
            | EmployeeEvent.CreatedAccountOwner e ->
               getEmailActor mailbox.System
               <! EmailActor.EmailMessage.EmployeeInvite {
                  Name = employee.Name
                  Email = employee.Email
                  Token = e.Data.InviteToken
               }
            | EmployeeEvent.CreatedEmployee _ ->
               match employee.Status with
               | EmployeeStatus.PendingInviteConfirmation token ->
                  getEmailActor mailbox.System
                  <! EmailActor.EmailMessage.EmployeeInvite {
                     Name = employee.Name
                     Email = employee.Email
                     Token = token
                  }
               | _ -> ()
            | EmployeeEvent.InvitationTokenRefreshed e ->
               getEmailActor mailbox.System
               <! EmailActor.EmailMessage.EmployeeInvite {
                  Name = employee.Name
                  Email = employee.Email
                  Token = e.Data.InviteToken
               }
            | EmployeeEvent.InvitationApproved e ->
               getEmailActor mailbox.System
               <! EmailActor.EmailMessage.EmployeeInvite {
                  Name = employee.Name
                  Email = employee.Email
                  Token = e.Data.InviteToken
               }
            | EmployeeEvent.InvitationConfirmed e ->
               for task in employee.OnboardingTasks do
                  match task with
                  | EmployeeOnboardingTask.CreateCard info ->
                     let msg =
                        CreateCardCommand.create {
                           AccountId = info.LinkedAccountId
                           DailyPurchaseLimit = Some info.DailyPurchaseLimit
                           MonthlyPurchaseLimit = Some info.MonthlyPurchaseLimit
                           PersonName = employee.Name
                           CardNickname = None
                           OrgId = employee.OrgId
                           EmployeeId = employee.EmployeeId
                           CardId = CardId <| Guid.NewGuid()
                           Virtual = true
                           CardType = CardType.Debit
                           InitiatedBy = e.InitiatedById
                        }
                        |> EmployeeCommand.CreateCard
                        |> EmployeeMessage.StateChange

                     mailbox.Parent() <! msg
            | EmployeeEvent.UpdatedRole e ->
               match e.Data.CardInfo with
               | Some info ->
                  let msg =
                     CreateCardCommand.create {
                        AccountId = info.LinkedAccountId
                        DailyPurchaseLimit = Some info.DailyPurchaseLimit
                        MonthlyPurchaseLimit = Some info.MonthlyPurchaseLimit
                        PersonName = employee.Name
                        CardNickname = None
                        OrgId = employee.OrgId
                        EmployeeId = employee.EmployeeId
                        CardId = CardId <| Guid.NewGuid()
                        Virtual = true
                        CardType = CardType.Debit
                        InitiatedBy = e.InitiatedById
                     }
                     |> EmployeeCommand.CreateCard
                     |> EmployeeMessage.StateChange

                  mailbox.Parent() <! msg
               | None -> ()
            | EmployeeEvent.DebitRequested e ->
               let accountId = e.Data.Info.AccountId
               let info = e.Data.Info

               let cmd =
                  DebitCommand.create
                     (accountId, e.OrgId)
                     e.CorrelationId
                     e.InitiatedById
                     {
                        Date = info.Date
                        Amount = info.Amount
                        Origin = info.Origin
                        Reference = info.Reference
                        EmployeePurchaseReference = {
                           EmployeeName = employee.Name
                           EmployeeCardNumberLast4 = info.CardNumberLast4
                           EmployeeId = info.EmployeeId
                           CardId = info.CardId
                        }
                     }

               // Notify associated company account actor of
               // debit request and wait for approval before
               // sending a response to issuing card network.
               getAccountRef accountId
               <! AccountMessage.StateChange(AccountCommand.Debit cmd)
            | EmployeeEvent.DebitApproved e ->
               // Notify card network which issued the debit request
               // to our bank.
               ()
            | EmployeeEvent.DebitDeclined e ->
               // Notify card network which issued the debit request
               // to our bank.
               ()
            | _ -> ()

            return! loop <| Some state
         | :? SnapshotOffer as o -> return! loop <| Some(unbox o.Snapshot)
         | :? ConfirmableMessageEnvelope as envelope ->
            match envelope.Message with
            | :? EmployeeMessage as msg ->
               match msg with
               | StateChange cmd ->
                  let validation = Employee.stateTransition state cmd

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
            | Delete ->
               let newState = {
                  state with
                     Info.Status = EmployeeStatus.ReadyForDelete
               }

               return! loop (Some newState) <@> DeleteMessages Int64.MaxValue
         // Event replay on actor start
         | :? EmployeeEvent as e when mailbox.IsRecovering() ->
            return! loop <| Some(Employee.applyEvent state e)
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

let isPersistableMessage (msg: obj) =
   match msg with
   | :? EmployeeMessage as msg ->
      match msg with
      | EmployeeMessage.StateChange _ -> true
      | _ -> false
   | _ -> false

let initProps
   (supervisorOpts: PersistenceSupervisorOptions)
   (persistenceId: string)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   =
   persistenceSupervisor
      supervisorOpts
      isPersistableMessage
      (actorProps getAccountRef EmailActor.get)
      persistenceId

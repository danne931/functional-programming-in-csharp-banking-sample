[<RequireQualifiedAccess>]
module Account

open Validus

open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes
open Lib.Time

let dailyDebitAccrued state (evt: BankEvent<DebitedAccount>) : decimal =
   // When applying a new event to the cached Account & the
   // last debit event did not occur today...
   // -> Ignore the cached DailyDebitAccrued
   let accrued =
      if
         state.LastDebitDate.IsSome
         && DateTime.isToday state.LastDebitDate.Value
      then
         state.DailyDebitAccrued
      else
         0m

   // When accumulating events into Account aggregate...
   // -> Ignore debits older than a day
   if DateTime.isToday evt.Data.Date then
      accrued + evt.Data.Amount
   else
      accrued

module TransferLimits =
   let DailyInternalLimit = 999_999_999m
   let DailyDomesticLimit = 100_000m

   let exceedsDailyInternalTransferLimit
      (account: Account)
      (cmd: InternalTransferCommand)
      =
      DateTime.isToday cmd.Data.ScheduledDate
      && account.DailyInternalTransferAccrued + cmd.Data.Amount > DailyInternalLimit

   let exceedsDailyDomesticTransferLimit
      (account: Account)
      (cmd: DomesticTransferCommand)
      =
      DateTime.isToday cmd.Data.ScheduledDate
      && account.DailyDomesticTransferAccrued + cmd.Data.Amount > DailyDomesticLimit

   let accruedDailyInternalTransfers
      (account: Account)
      (evt: BankEvent<InternalTransferPending>)
      =
      let accrued =
         match account.LastInternalTransferDate with
         | Some date when DateTime.isToday date ->
            account.DailyInternalTransferAccrued
         | _ -> 0m

      if DateTime.isToday evt.Data.ScheduledDate then
         accrued + evt.Data.Amount
      else
         accrued

   let accruedDailyDomesticTransfers
      (account: Account)
      (evt: BankEvent<DomesticTransferPending>)
      =
      let accrued =
         match account.LastDomesticTransferDate with
         | Some date when DateTime.isToday date ->
            account.DailyDomesticTransferAccrued
         | _ -> 0m

      let info = evt.Data.BaseInfo

      if DateTime.isToday info.ScheduledDate then
         accrued + info.Amount
      else
         accrued

let applyEvent (state: Account) (evt: AccountEvent) =
   let newState =
      match evt with
      | BillingCycleStarted e -> {
         state with
            LastBillingCycleDate = Some e.Timestamp
            Events = []
        }
      | CreatedAccount e -> {
         AccountId = AccountId.fromEntityId e.EntityId
         AccountNumber = e.Data.AccountNumber
         RoutingNumber = e.Data.RoutingNumber
         OrgId = e.OrgId
         Email = e.Data.Email
         FirstName = e.Data.FirstName
         LastName = e.Data.LastName
         Currency = e.Data.Currency
         Balance = e.Data.Balance
         Status = AccountStatus.Active
         DailyDebitLimit = 2000m
         DailyDebitAccrued = 0m
         DailyInternalTransferAccrued = 0m
         DailyDomesticTransferAccrued = 0m
         LastDebitDate = None
         LastInternalTransferDate = None
         LastDomesticTransferDate = None
         LastBillingCycleDate = None
         InternalTransferRecipients = Map.empty
         InProgressInternalTransfers = Map.empty
         InternalTransferSenders = Map.empty
         DomesticTransferRecipients = Map.empty
         InProgressDomesticTransfers = Map.empty
         FailedDomesticTransfers = Map.empty
         MaintenanceFeeCriteria = {
            QualifyingDepositFound = false
            DailyBalanceThreshold = false
         }
         Events = []
         CardLocked = false
        }
      | AccountEvent.AccountClosed _ -> {
         state with
            Status = AccountStatus.Closed
        }
      | DepositedCash e -> {
         state with
            Balance = state.Balance + e.Data.Amount
            MaintenanceFeeCriteria =
               MaintenanceFee.fromDeposit
                  state.MaintenanceFeeCriteria
                  e.Data.Amount
        }
      | DebitedAccount e ->
         let balance = state.Balance - e.Data.Amount

         {
            state with
               Balance = balance
               DailyDebitAccrued = dailyDebitAccrued state e
               LastDebitDate = Some e.Data.Date
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebit state.MaintenanceFeeCriteria balance
         }
      | MaintenanceFeeDebited e ->
         let balance = state.Balance - e.Data.Amount

         {
            state with
               Balance = balance
               MaintenanceFeeCriteria = MaintenanceFee.reset balance
         }
      | MaintenanceFeeSkipped _ -> {
         state with
            MaintenanceFeeCriteria = MaintenanceFee.reset state.Balance
        }
      | DailyDebitLimitUpdated e -> {
         state with
            DailyDebitLimit = e.Data.DebitLimit
        }
      | LockedCard _ -> { state with CardLocked = true }
      | UnlockedCard _ -> { state with CardLocked = false }
      | DomesticTransferPending e ->
         let info = e.Data.BaseInfo
         let balance = state.Balance - info.Amount

         {
            state with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebit state.MaintenanceFeeCriteria balance
               // When reattempting a failed transfer, the transfer will be
               // moved from the failed map to the in-progress map.
               FailedDomesticTransfers =
                  Map.remove e.CorrelationId state.FailedDomesticTransfers
               InProgressDomesticTransfers =
                  Map.add
                     e.CorrelationId
                     (TransferEventToDomesticTransfer.fromPending e)
                     state.InProgressDomesticTransfers
               DailyDomesticTransferAccrued =
                  TransferLimits.accruedDailyDomesticTransfers state e
               LastDomesticTransferDate = Some info.ScheduledDate
         }
      | DomesticTransferProgress e -> {
         state with
            InProgressDomesticTransfers =
               Map.change
                  e.CorrelationId
                  (Option.map (fun txn -> { txn with Status = e.Data.Status }))
                  state.InProgressDomesticTransfers
        }
      | DomesticTransferApproved e -> {
         state with
            InProgressDomesticTransfers =
               Map.remove e.CorrelationId state.InProgressDomesticTransfers
            // When a domestic transfer is retried & approved after being
            // rejected due to invalid account details then update the
            // recipient status to Confirmed.
            DomesticTransferRecipients =
               Map.change
                  e.Data.BaseInfo.Recipient.AccountId
                  (Option.map (fun recipient ->
                     if
                        recipient.Status = RecipientRegistrationStatus.InvalidAccount
                     then
                        {
                           recipient with
                              Status = RecipientRegistrationStatus.Confirmed
                        }
                     else
                        recipient))
                  state.DomesticTransferRecipients
        }
      | DomesticTransferRejected e ->
         let info = e.Data.BaseInfo
         let balance = state.Balance + info.Amount

         let updatedRecipientStatus =
            match e.Data.Reason with
            | TransferDeclinedReason.InvalidAccountInfo ->
               Some RecipientRegistrationStatus.InvalidAccount
            | TransferDeclinedReason.AccountClosed ->
               Some RecipientRegistrationStatus.Closed
            | _ -> None

         let updatedRecipients =
            match updatedRecipientStatus with
            | None -> state.DomesticTransferRecipients
            | Some status ->
               Map.change
                  info.Recipient.AccountId
                  (Option.map (fun recipient -> {
                     recipient with
                        Status = status
                  }))
                  state.DomesticTransferRecipients

         {
            state with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebitReversal
                     state.MaintenanceFeeCriteria
                     balance
               // When a domestic transfer fails it will be moved from the
               // in-progress map to the failed map.
               InProgressDomesticTransfers =
                  Map.remove e.CorrelationId state.InProgressDomesticTransfers
               FailedDomesticTransfers =
                  Map.add
                     e.CorrelationId
                     (TransferEventToDomesticTransfer.fromRejection e)
                     state.FailedDomesticTransfers
               DomesticTransferRecipients = updatedRecipients
               // Revert daily accrued transfer sum
               DailyDomesticTransferAccrued =
                  if DateTime.isToday info.ScheduledDate then
                     state.DailyDomesticTransferAccrued - info.Amount
                  else
                     state.DailyDomesticTransferAccrued
         }
      | InternalTransferPending e ->
         let balance = state.Balance - e.Data.Amount

         {
            state with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebit state.MaintenanceFeeCriteria balance
               InProgressInternalTransfers =
                  Map.add e.CorrelationId e state.InProgressInternalTransfers
               DailyInternalTransferAccrued =
                  TransferLimits.accruedDailyInternalTransfers state e
               LastInternalTransferDate = Some e.Data.ScheduledDate
         }
      | InternalTransferApproved e -> {
         state with
            InProgressInternalTransfers =
               Map.remove e.CorrelationId state.InProgressInternalTransfers
        }
      | InternalTransferRejected e ->
         let balance = state.Balance + e.Data.Amount

         let updatedRecipientStatus =
            match e.Data.Reason with
            | TransferDeclinedReason.InvalidAccountInfo ->
               Some RecipientRegistrationStatus.InvalidAccount
            | TransferDeclinedReason.AccountClosed ->
               Some RecipientRegistrationStatus.Closed
            | _ -> None

         let updatedRecipients =
            match updatedRecipientStatus with
            | None -> state.InternalTransferRecipients
            | Some status ->
               Map.change
                  e.Data.RecipientId
                  (Option.map (fun recipient -> {
                     recipient with
                        Status = status
                  }))
                  state.InternalTransferRecipients

         {
            state with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebitReversal
                     state.MaintenanceFeeCriteria
                     balance
               InProgressInternalTransfers =
                  Map.remove e.CorrelationId state.InProgressInternalTransfers
               InternalTransferRecipients = updatedRecipients
               DailyInternalTransferAccrued =
                  if DateTime.isToday e.Data.ScheduledDate then
                     state.DailyInternalTransferAccrued - e.Data.Amount
                  else
                     state.DailyInternalTransferAccrued
         }
      | TransferDeposited e -> {
         state with
            Balance = state.Balance + e.Data.Amount
            MaintenanceFeeCriteria =
               MaintenanceFee.fromDeposit
                  state.MaintenanceFeeCriteria
                  e.Data.Amount
        }
      | InternalTransferRecipient e ->
         let recipient = e.Data.Recipient

         {
            state with
               InternalTransferRecipients =
                  state.InternalTransferRecipients.Add(
                     recipient.AccountId,
                     recipient
                  )
         }
      | DomesticTransferRecipient e ->
         let recipient = e.Data.Recipient

         {
            state with
               DomesticTransferRecipients =
                  state.DomesticTransferRecipients.Add(
                     recipient.AccountId,
                     recipient
                  )
         }
      | EditedDomesticTransferRecipient e ->
         let recipient = e.Data.Recipient

         {
            state with
               DomesticTransferRecipients =
                  state.DomesticTransferRecipients.Change(
                     recipient.AccountId,
                     Option.map (fun _ -> recipient)
                  )
         }
      | InternalRecipientDeactivated e -> {
         state with
            InternalTransferRecipients =
               Map.change
                  e.Data.RecipientId
                  (Option.map (fun acct -> {
                     acct with
                        Status = RecipientRegistrationStatus.Closed
                  }))
                  state.InternalTransferRecipients
        }
      | RecipientNicknamed e ->
         match e.Data.RecipientAccountEnvironment with
         | RecipientAccountEnvironment.Internal -> {
            state with
               InternalTransferRecipients =
                  Map.change
                     e.Data.RecipientId
                     (Option.map (fun acct -> {
                        acct with
                           Nickname = e.Data.Nickname
                     }))
                     state.InternalTransferRecipients
           }
         | RecipientAccountEnvironment.Domestic -> {
            state with
               DomesticTransferRecipients =
                  Map.change
                     e.Data.RecipientId
                     (Option.map (fun acct -> {
                        acct with
                           Nickname = e.Data.Nickname
                     }))
                     state.DomesticTransferRecipients
           }
      | InternalSenderRegistered e -> {
         state with
            InternalTransferSenders =
               Map.add
                  e.Data.Sender.AccountId
                  e.Data.Sender
                  state.InternalTransferSenders
        }

   {
      newState with
         Events = evt :: newState.Events
   }

module private StateTransition =
   let transitionErr (err: StateTransitionError) =
      Error <| StateTransitionError err

   let map
      (eventTransform: BankEvent<'t> -> AccountEvent)
      (state: Account)
      (eventValidation: ValidationResult<BankEvent<'t>>)
      =
      eventValidation
      |> Result.mapError ValidationError
      |> Result.map (fun evt ->
         let evt = eventTransform evt
         (evt, applyEvent state evt))

   let create (state: Account) (cmd: CreateAccountCommand) =
      if state.Status <> AccountStatus.Pending then
         transitionErr AccountNotReadyToActivate
      else
         map CreatedAccount state (CreateAccountCommand.toEvent cmd)

   let startBillingcycle (state: Account) (cmd: StartBillingCycleCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map BillingCycleStarted state (StartBillingCycleCommand.toEvent cmd)

   let deposit (state: Account) (cmd: DepositCashCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map DepositedCash state (DepositCashCommand.toEvent cmd)

   let limitDailyDebits (state: Account) (cmd: LimitDailyDebitsCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map DailyDebitLimitUpdated state (LimitDailyDebitsCommand.toEvent cmd)

   let lockCard (state: Account) (cmd: LockCardCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map LockedCard state (LockCardCommand.toEvent cmd)

   let unlockCard (state: Account) (cmd: UnlockCardCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map UnlockedCard state (UnlockCardCommand.toEvent cmd)

   let debit (state: Account) (cmd: DebitCommand) =
      let input = cmd.Data

      if state.CardLocked then
         transitionErr AccountCardLocked
      elif state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      elif
         DateTime.isToday input.Date
         && state.DailyDebitAccrued + input.Amount > state.DailyDebitLimit
      then
         transitionErr <| ExceededDailyDebit state.DailyDebitLimit
      else
         map DebitedAccount state (DebitCommand.toEvent cmd)

   let maintenanceFee (state: Account) (cmd: MaintenanceFeeCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.Balance - cmd.Data.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      else
         map MaintenanceFeeDebited state (MaintenanceFeeCommand.toEvent cmd)

   let skipMaintenanceFee (state: Account) (cmd: SkipMaintenanceFeeCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map MaintenanceFeeSkipped state (SkipMaintenanceFeeCommand.toEvent cmd)

   let internalTransfer (state: Account) (cmd: InternalTransferCommand) =
      let input = cmd.Data

      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      elif
         state.InternalTransferRecipients
         |> Map.containsKey input.RecipientId
         |> not
      then
         transitionErr RecipientRegistrationRequired
      elif TransferLimits.exceedsDailyInternalTransferLimit state cmd then
         TransferLimits.DailyInternalLimit
         |> ExceededDailyInternalTransferLimit
         |> transitionErr
      else
         map InternalTransferPending state (InternalTransferCommand.toEvent cmd)

   let approveInternalTransfer
      (state: Account)
      (cmd: ApproveInternalTransferCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind cmd.CorrelationId state.InProgressInternalTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            InternalTransferApproved
            state
            (ApproveInternalTransferCommand.toEvent cmd)

   let rejectInternalTransfer
      (state: Account)
      (cmd: RejectInternalTransferCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind cmd.CorrelationId state.InProgressInternalTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            InternalTransferRejected
            state
            (RejectInternalTransferCommand.toEvent cmd)

   let domesticTransfer (state: Account) (cmd: DomesticTransferCommand) =
      let input = cmd.Data

      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      elif
         state.DomesticTransferRecipients
         |> Map.containsKey input.Recipient.AccountId
         |> not
      then
         transitionErr RecipientRegistrationRequired
      elif TransferLimits.exceedsDailyDomesticTransferLimit state cmd then
         TransferLimits.DailyDomesticLimit
         |> ExceededDailyDomesticTransferLimit
         |> transitionErr
      else
         map DomesticTransferPending state (DomesticTransferCommand.toEvent cmd)

   let domesticTransferProgress
      (state: Account)
      (cmd: UpdateDomesticTransferProgressCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         let existing =
            Map.tryFind cmd.CorrelationId state.InProgressDomesticTransfers

         match existing with
         | None ->
            map
               DomesticTransferProgress
               state
               (UpdateDomesticTransferProgressCommand.toEvent cmd)
         | Some txn ->
            let existingStatus = txn.Status

            if existingStatus = cmd.Data.Status then
               transitionErr TransferProgressNoChange
            else
               map
                  DomesticTransferProgress
                  state
                  (UpdateDomesticTransferProgressCommand.toEvent cmd)

   let approveDomesticTransfer
      (state: Account)
      (cmd: ApproveDomesticTransferCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind cmd.CorrelationId state.InProgressDomesticTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            DomesticTransferApproved
            state
            (ApproveDomesticTransferCommand.toEvent cmd)

   let rejectDomesticTransfer
      (state: Account)
      (cmd: RejectDomesticTransferCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind cmd.CorrelationId state.InProgressDomesticTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            DomesticTransferRejected
            state
            (RejectDomesticTransferCommand.toEvent cmd)

   let registerInternalTransferRecipient
      (state: Account)
      (cmd: RegisterInternalTransferRecipientCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.InternalTransferRecipients.ContainsKey cmd.Data.AccountId then
         transitionErr RecipientRegistered
      else
         map InternalTransferRecipient state
         <| RegisterInternalTransferRecipientCommand.toEvent cmd

   let registerDomesticTransferRecipient
      (state: Account)
      (cmd: RegisterDomesticTransferRecipientCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         state.DomesticTransferRecipients
         |> Map.exists (fun _ recipient ->
            string recipient.AccountNumber = cmd.Data.AccountNumber
            && string recipient.RoutingNumber = cmd.Data.RoutingNumber)
      then
         transitionErr RecipientRegistered
      else
         map DomesticTransferRecipient state
         <| RegisterDomesticTransferRecipientCommand.toEvent cmd

   let editDomesticTransferRecipient
      (state: Account)
      (cmd: EditDomesticTransferRecipientCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         state.DomesticTransferRecipients
         |> Map.tryFind cmd.Data.RecipientWithoutAppliedUpdates.AccountId
         |> Option.bind (fun recipient ->
            if recipient.Status = RecipientRegistrationStatus.Closed then
               Some recipient
            else
               None)
         |> Option.isSome
      then
         transitionErr RecipientDeactivated
      else
         map EditedDomesticTransferRecipient state
         <| EditDomesticTransferRecipientCommand.toEvent cmd

   let registerInternalSender
      (state: Account)
      (cmd: RegisterInternalSenderCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         state.InternalTransferSenders.ContainsKey cmd.Data.Sender.AccountId
      then
         transitionErr SenderRegistered
      else
         map
            InternalSenderRegistered
            state
            (RegisterInternalSenderCommand.toEvent cmd)

   let deactivateInternalRecipient
      (state: Account)
      (cmd: DeactivateInternalRecipientCommand)
      =
      let recipientId = cmd.Data.RecipientId

      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         not <| Map.containsKey recipientId state.InternalTransferRecipients
      then
         transitionErr RecipientNotFound
      else
         let recipient = state.InternalTransferRecipients[recipientId]

         if recipient.Status = RecipientRegistrationStatus.Closed then
            transitionErr RecipientDeactivated
         else
            map
               InternalRecipientDeactivated
               state
               (DeactivateInternalRecipientCommand.toEvent cmd)

   let depositTransfer (state: Account) (cmd: DepositTransferCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map TransferDeposited state <| (DepositTransferCommand.toEvent cmd)

   let closeAccount (state: Account) (cmd: CloseAccountCommand) =
      map AccountEvent.AccountClosed state (CloseAccountCommand.toEvent cmd)

   let nicknameRecipient (state: Account) (cmd: NicknameRecipientCommand) =
      let recipientExists, recipientIsActive =
         match cmd.Data.RecipientAccountEnvironment with
         | RecipientAccountEnvironment.Internal ->
            match
               state.InternalTransferRecipients.TryFind cmd.Data.RecipientId
            with
            | None -> false, false
            | Some recipient ->
               true, recipient.Status <> RecipientRegistrationStatus.Closed
         | RecipientAccountEnvironment.Domestic ->
            match
               state.DomesticTransferRecipients.TryFind cmd.Data.RecipientId
            with
            | None -> false, false
            | Some recipient ->
               true, recipient.Status <> RecipientRegistrationStatus.Closed

      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif not recipientExists then
         transitionErr RecipientNotFound
      else if not recipientIsActive then
         transitionErr RecipientDeactivated
      else
         map RecipientNicknamed state (NicknameRecipientCommand.toEvent cmd)

let stateTransition (state: Account) (command: AccountCommand) =
   match command with
   | CreateAccount cmd -> StateTransition.create state cmd
   | StartBillingCycle cmd -> StateTransition.startBillingcycle state cmd
   | DepositCash cmd -> StateTransition.deposit state cmd
   | Debit cmd -> StateTransition.debit state cmd
   | MaintenanceFee cmd -> StateTransition.maintenanceFee state cmd
   | SkipMaintenanceFee cmd -> StateTransition.skipMaintenanceFee state cmd
   | LimitDailyDebits cmd -> StateTransition.limitDailyDebits state cmd
   | LockCard cmd -> StateTransition.lockCard state cmd
   | UnlockCard cmd -> StateTransition.unlockCard state cmd
   | InternalTransfer cmd -> StateTransition.internalTransfer state cmd
   | ApproveInternalTransfer cmd ->
      StateTransition.approveInternalTransfer state cmd
   | RejectInternalTransfer cmd ->
      StateTransition.rejectInternalTransfer state cmd
   | DepositTransfer cmd -> StateTransition.depositTransfer state cmd
   | RegisterInternalTransferRecipient cmd ->
      StateTransition.registerInternalTransferRecipient state cmd
   | RegisterInternalSender cmd ->
      StateTransition.registerInternalSender state cmd
   | DeactivateInternalRecipient cmd ->
      StateTransition.deactivateInternalRecipient state cmd
   | RegisterDomesticTransferRecipient cmd ->
      StateTransition.registerDomesticTransferRecipient state cmd
   | EditDomesticTransferRecipient cmd ->
      StateTransition.editDomesticTransferRecipient state cmd
   | DomesticTransfer cmd -> StateTransition.domesticTransfer state cmd
   | ApproveDomesticTransfer cmd ->
      StateTransition.approveDomesticTransfer state cmd
   | RejectDomesticTransfer cmd ->
      StateTransition.rejectDomesticTransfer state cmd
   | UpdateDomesticTransferProgress cmd ->
      StateTransition.domesticTransferProgress state cmd
   | NicknameRecipient cmd -> StateTransition.nicknameRecipient state cmd
   | CloseAccount cmd -> StateTransition.closeAccount state cmd

let empty = {
   AccountId = AccountId System.Guid.Empty
   OrgId = OrgId System.Guid.Empty
   Email = Email.empty
   FirstName = ""
   LastName = ""
   Currency = Currency.USD
   Status = AccountStatus.Pending
   Balance = 0m
   DailyDebitLimit = 2000m
   DailyDebitAccrued = 0m
   DailyInternalTransferAccrued = 0m
   DailyDomesticTransferAccrued = 0m
   LastDebitDate = None
   LastInternalTransferDate = None
   LastDomesticTransferDate = None
   LastBillingCycleDate = None
   InternalTransferRecipients = Map.empty
   InProgressInternalTransfers = Map.empty
   InternalTransferSenders = Map.empty
   DomesticTransferRecipients = Map.empty
   InProgressDomesticTransfers = Map.empty
   FailedDomesticTransfers = Map.empty
   MaintenanceFeeCriteria = {
      QualifyingDepositFound = false
      DailyBalanceThreshold = false
   }
   Events = []
   CardLocked = false
   AccountNumber = AccountNumber <| System.Int64.Parse "123456789123456"
   RoutingNumber = RoutingNumber 123456789
}

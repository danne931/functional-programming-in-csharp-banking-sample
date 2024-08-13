[<RequireQualifiedAccess>]
module Account

open Validus
open System

open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes
open Lib.Time

module TransferLimits =
   let DailyInternalLimit = 999_999_999m
   let DailyDomesticLimit = 100_000m

   let private transferAccrued
      (satisfiesDate: DateTime -> bool)
      (eventToAccrual: AccountEvent -> (DateTime * decimal) option)
      (events: AccountEvent list)
      : decimal
      =
      List.fold
         (fun acc evt ->
            match eventToAccrual evt with
            | Some(date, amount) ->
               if satisfiesDate date then acc + amount else acc
            | None -> acc)
         0m
         events

   let dailyInternalTransferAccrued =
      transferAccrued DateTime.isToday (function
         | AccountEvent.InternalTransferPending e ->
            let info = e.Data.BaseInfo
            Some(info.ScheduledDate, info.Amount)
         | AccountEvent.InternalTransferRejected e ->
            let info = e.Data.BaseInfo
            Some(info.ScheduledDate, -info.Amount)
         | _ -> None)

   let dailyDomesticTransferAccrued =
      transferAccrued DateTime.isToday (function
         | AccountEvent.DomesticTransferPending e ->
            let info = e.Data.BaseInfo
            Some(info.ScheduledDate, info.Amount)
         | AccountEvent.DomesticTransferRejected e ->
            let info = e.Data.BaseInfo
            Some(info.ScheduledDate, -info.Amount)
         | _ -> None)

   let exceedsDailyInternalTransferLimit
      (evts: AccountEvent list)
      (cmd: InternalTransferCommand)
      =
      let info = cmd.Data.BaseInfo

      DateTime.isToday info.ScheduledDate
      && (dailyInternalTransferAccrued evts) + info.Amount > DailyInternalLimit

   let exceedsDailyDomesticTransferLimit
      (evts: AccountEvent list)
      (cmd: DomesticTransferCommand)
      =
      DateTime.isToday cmd.Data.ScheduledDate
      && (dailyDomesticTransferAccrued evts) + cmd.Data.Amount > DailyDomesticLimit

let applyEvent (state: AccountWithEvents) (evt: AccountEvent) =
   let account = state.Info

   let updatedAccount =
      match evt with
      | BillingCycleStarted e -> {
         account with
            LastBillingCycleDate = Some e.Timestamp
        }
      | CreatedAccount e -> {
         AccountId = AccountId.fromEntityId e.EntityId
         AccountNumber = e.Data.AccountNumber
         RoutingNumber = e.Data.RoutingNumber
         OrgId = e.OrgId
         Name = e.Data.Name
         Depository = e.Data.Depository
         Currency = e.Data.Currency
         Balance = e.Data.Balance
         Status = AccountStatus.Active
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
        }
      | AccountEvent.AccountClosed _ -> {
         account with
            Status = AccountStatus.Closed
        }
      | DepositedCash e -> {
         account with
            Balance = account.Balance + e.Data.Amount
            MaintenanceFeeCriteria =
               MaintenanceFee.fromDeposit
                  account.MaintenanceFeeCriteria
                  e.Data.Amount
        }
      | DebitedAccount e ->
         let balance = account.Balance - e.Data.Amount

         {
            account with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebit
                     account.MaintenanceFeeCriteria
                     balance
         }
      | MaintenanceFeeDebited e ->
         let balance = account.Balance - e.Data.Amount

         {
            account with
               Balance = balance
               MaintenanceFeeCriteria = MaintenanceFee.reset balance
         }
      | MaintenanceFeeSkipped _ -> {
         account with
            MaintenanceFeeCriteria = MaintenanceFee.reset account.Balance
        }
      | DomesticTransferPending e ->
         let info = e.Data.BaseInfo
         let balance = account.Balance - info.Amount

         {
            account with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebit
                     account.MaintenanceFeeCriteria
                     balance
               // When reattempting a failed transfer, the transfer will be
               // moved from the failed map to the in-progress map.
               FailedDomesticTransfers =
                  Map.remove e.CorrelationId account.FailedDomesticTransfers
               InProgressDomesticTransfers =
                  Map.add
                     e.CorrelationId
                     (TransferEventToDomesticTransfer.fromPending e)
                     account.InProgressDomesticTransfers
         }
      | DomesticTransferProgress e -> {
         account with
            InProgressDomesticTransfers =
               Map.change
                  e.CorrelationId
                  (Option.map (fun txn -> { txn with Status = e.Data.Status }))
                  account.InProgressDomesticTransfers
        }
      | DomesticTransferApproved e -> {
         account with
            InProgressDomesticTransfers =
               Map.remove e.CorrelationId account.InProgressDomesticTransfers
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
                  account.DomesticTransferRecipients
        }
      | DomesticTransferRejected e ->
         let info = e.Data.BaseInfo
         let balance = account.Balance + info.Amount

         let updatedRecipientStatus =
            match e.Data.Reason with
            | TransferDeclinedReason.InvalidAccountInfo ->
               Some RecipientRegistrationStatus.InvalidAccount
            | TransferDeclinedReason.AccountClosed ->
               Some RecipientRegistrationStatus.Closed
            | _ -> None

         let updatedRecipients =
            match updatedRecipientStatus with
            | None -> account.DomesticTransferRecipients
            | Some status ->
               Map.change
                  info.Recipient.AccountId
                  (Option.map (fun recipient -> {
                     recipient with
                        Status = status
                  }))
                  account.DomesticTransferRecipients

         {
            account with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebitReversal
                     account.MaintenanceFeeCriteria
                     balance
               // When a domestic transfer fails it will be moved from the
               // in-progress map to the failed map.
               InProgressDomesticTransfers =
                  Map.remove e.CorrelationId account.InProgressDomesticTransfers
               FailedDomesticTransfers =
                  Map.add
                     e.CorrelationId
                     (TransferEventToDomesticTransfer.fromRejection e)
                     account.FailedDomesticTransfers
               DomesticTransferRecipients = updatedRecipients
         }
      | InternalTransferPending e ->
         let balance = account.Balance - e.Data.BaseInfo.Amount

         {
            account with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebit
                     account.MaintenanceFeeCriteria
                     balance
               InProgressInternalTransfers =
                  Map.add e.CorrelationId e account.InProgressInternalTransfers
         }
      | InternalTransferApproved e -> {
         account with
            InProgressInternalTransfers =
               Map.remove e.CorrelationId account.InProgressInternalTransfers
        }
      | InternalTransferRejected e ->
         let balance = account.Balance + e.Data.BaseInfo.Amount

         let updatedRecipientStatus =
            match e.Data.Reason with
            | TransferDeclinedReason.InvalidAccountInfo ->
               Some RecipientRegistrationStatus.InvalidAccount
            | TransferDeclinedReason.AccountClosed ->
               Some RecipientRegistrationStatus.Closed
            | _ -> None

         let updatedRecipients =
            match updatedRecipientStatus with
            | None -> account.InternalTransferRecipients
            | Some status ->
               Map.change
                  e.Data.BaseInfo.RecipientId
                  (Option.map (fun recipient -> {
                     recipient with
                        Status = status
                  }))
                  account.InternalTransferRecipients

         {
            account with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebitReversal
                     account.MaintenanceFeeCriteria
                     balance
               InProgressInternalTransfers =
                  Map.remove e.CorrelationId account.InProgressInternalTransfers
               InternalTransferRecipients = updatedRecipients
         }
      | TransferDeposited e -> {
         account with
            Balance = account.Balance + e.Data.Amount
            MaintenanceFeeCriteria =
               MaintenanceFee.fromDeposit
                  account.MaintenanceFeeCriteria
                  e.Data.Amount
        }
      | InternalTransferRecipient e ->
         let recipient = e.Data.Recipient

         {
            account with
               InternalTransferRecipients =
                  account.InternalTransferRecipients.Add(
                     recipient.AccountId,
                     recipient
                  )
         }
      | DomesticTransferRecipient e ->
         let recipient = e.Data.Recipient

         {
            account with
               DomesticTransferRecipients =
                  account.DomesticTransferRecipients.Add(
                     recipient.AccountId,
                     recipient
                  )
         }
      | EditedDomesticTransferRecipient e ->
         let recipient = e.Data.Recipient

         {
            account with
               DomesticTransferRecipients =
                  account.DomesticTransferRecipients.Change(
                     recipient.AccountId,
                     Option.map (fun _ -> recipient)
                  )
         }
      | InternalRecipientDeactivated e -> {
         account with
            InternalTransferRecipients =
               Map.change
                  e.Data.RecipientId
                  (Option.map (fun acct -> {
                     acct with
                        Status = RecipientRegistrationStatus.Closed
                  }))
                  account.InternalTransferRecipients
        }
      | RecipientNicknamed e ->
         match e.Data.RecipientAccountEnvironment with
         | RecipientAccountEnvironment.Internal -> {
            account with
               InternalTransferRecipients =
                  Map.change
                     e.Data.RecipientId
                     (Option.map (fun acct -> {
                        acct with
                           Nickname = e.Data.Nickname
                     }))
                     account.InternalTransferRecipients
           }
         | RecipientAccountEnvironment.Domestic -> {
            account with
               DomesticTransferRecipients =
                  Map.change
                     e.Data.RecipientId
                     (Option.map (fun acct -> {
                        acct with
                           Nickname = e.Data.Nickname
                     }))
                     account.DomesticTransferRecipients
           }
      | InternalSenderRegistered e -> {
         account with
            InternalTransferSenders =
               Map.add
                  e.Data.Sender.AccountId
                  e.Data.Sender
                  account.InternalTransferSenders
        }

   let updatedEvents = evt :: state.Events

   let updatedEvents =
      match evt, account.LastBillingCycleDate with
      | MaintenanceFeeDebited _, Some date
      | MaintenanceFeeSkipped _, Some date ->
         // Keep events that occurred after the previous billing statement period.
         let cutoff = DateTime(date.Year, date.Month, 1).ToUniversalTime()

         state.Events
         |> List.filter (fun evt ->
            let _, env = AccountEnvelope.unwrap evt
            env.Timestamp >= cutoff)
      | _ -> updatedEvents

   {
      state with
         Info = updatedAccount
         Events = updatedEvents
   }

module private StateTransition =
   let transitionErr (err: AccountStateTransitionError) =
      Error <| AccountStateTransitionError err

   let map
      (eventTransform: BankEvent<'t> -> AccountEvent)
      (state: AccountWithEvents)
      (eventValidation: ValidationResult<BankEvent<'t>>)
      =
      eventValidation
      |> Result.mapError ValidationError
      |> Result.map (fun evt ->
         let evt = eventTransform evt
         (evt, applyEvent state evt))

   let create (state: AccountWithEvents) (cmd: CreateAccountCommand) =
      if state.Info.Status <> AccountStatus.InitialEmptyState then
         transitionErr AccountNotReadyToActivate
      else
         map CreatedAccount state (CreateAccountCommand.toEvent cmd)

   let startBillingcycle
      (state: AccountWithEvents)
      (cmd: StartBillingCycleCommand)
      =
      if state.Info.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map BillingCycleStarted state (StartBillingCycleCommand.toEvent cmd)

   let deposit (state: AccountWithEvents) (cmd: DepositCashCommand) =
      if state.Info.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map DepositedCash state (DepositCashCommand.toEvent cmd)

   let debit (state: AccountWithEvents) (cmd: DebitCommand) =
      let input = cmd.Data
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif account.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance account.Balance
      else
         map DebitedAccount state (DebitCommand.toEvent cmd)

   let maintenanceFee (state: AccountWithEvents) (cmd: MaintenanceFeeCommand) =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif account.Balance - cmd.Data.Amount < 0m then
         transitionErr <| InsufficientBalance account.Balance
      else
         map MaintenanceFeeDebited state (MaintenanceFeeCommand.toEvent cmd)

   let skipMaintenanceFee
      (state: AccountWithEvents)
      (cmd: SkipMaintenanceFeeCommand)
      =
      if state.Info.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map MaintenanceFeeSkipped state (SkipMaintenanceFeeCommand.toEvent cmd)

   let internalTransfer
      (state: AccountWithEvents)
      (cmd: InternalTransferCommand)
      =
      let input = cmd.Data.BaseInfo
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif account.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance account.Balance
      elif
         account.InternalTransferRecipients
         |> Map.containsKey input.RecipientId
         |> not
      then
         transitionErr RecipientRegistrationRequired
      elif
         TransferLimits.exceedsDailyInternalTransferLimit state.Events cmd
      then
         TransferLimits.DailyInternalLimit
         |> ExceededDailyInternalTransferLimit
         |> transitionErr
      else
         map InternalTransferPending state (InternalTransferCommand.toEvent cmd)

   let approveInternalTransfer
      (state: AccountWithEvents)
      (cmd: ApproveInternalTransferCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind cmd.CorrelationId account.InProgressInternalTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            InternalTransferApproved
            state
            (ApproveInternalTransferCommand.toEvent cmd)

   let rejectInternalTransfer
      (state: AccountWithEvents)
      (cmd: RejectInternalTransferCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind cmd.CorrelationId account.InProgressInternalTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            InternalTransferRejected
            state
            (RejectInternalTransferCommand.toEvent cmd)

   let domesticTransfer
      (state: AccountWithEvents)
      (cmd: DomesticTransferCommand)
      =
      let input = cmd.Data
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif account.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance account.Balance
      elif
         account.DomesticTransferRecipients
         |> Map.containsKey input.Recipient.AccountId
         |> not
      then
         transitionErr RecipientRegistrationRequired
      elif
         TransferLimits.exceedsDailyDomesticTransferLimit state.Events cmd
      then
         TransferLimits.DailyDomesticLimit
         |> ExceededDailyDomesticTransferLimit
         |> transitionErr
      else
         map DomesticTransferPending state (DomesticTransferCommand.toEvent cmd)

   let domesticTransferProgress
      (state: AccountWithEvents)
      (cmd: UpdateDomesticTransferProgressCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         let existing =
            Map.tryFind cmd.CorrelationId account.InProgressDomesticTransfers

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
      (state: AccountWithEvents)
      (cmd: ApproveDomesticTransferCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind cmd.CorrelationId account.InProgressDomesticTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            DomesticTransferApproved
            state
            (ApproveDomesticTransferCommand.toEvent cmd)

   let rejectDomesticTransfer
      (state: AccountWithEvents)
      (cmd: RejectDomesticTransferCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind cmd.CorrelationId account.InProgressDomesticTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            DomesticTransferRejected
            state
            (RejectDomesticTransferCommand.toEvent cmd)

   let registerInternalTransferRecipient
      (state: AccountWithEvents)
      (cmd: RegisterInternalTransferRecipientCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         account.InternalTransferRecipients.ContainsKey cmd.Data.AccountId
      then
         transitionErr RecipientRegistered
      else
         map InternalTransferRecipient state
         <| RegisterInternalTransferRecipientCommand.toEvent cmd

   let registerDomesticTransferRecipient
      (state: AccountWithEvents)
      (cmd: RegisterDomesticTransferRecipientCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         account.DomesticTransferRecipients
         |> Map.exists (fun _ recipient ->
            string recipient.AccountNumber = cmd.Data.AccountNumber
            && string recipient.RoutingNumber = cmd.Data.RoutingNumber)
      then
         transitionErr RecipientRegistered
      else
         map DomesticTransferRecipient state
         <| RegisterDomesticTransferRecipientCommand.toEvent cmd

   let editDomesticTransferRecipient
      (state: AccountWithEvents)
      (cmd: EditDomesticTransferRecipientCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         account.DomesticTransferRecipients
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
      (state: AccountWithEvents)
      (cmd: RegisterInternalSenderCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         account.InternalTransferSenders.ContainsKey cmd.Data.Sender.AccountId
      then
         transitionErr SenderRegistered
      else
         map
            InternalSenderRegistered
            state
            (RegisterInternalSenderCommand.toEvent cmd)

   let deactivateInternalRecipient
      (state: AccountWithEvents)
      (cmd: DeactivateInternalRecipientCommand)
      =
      let recipientId = cmd.Data.RecipientId
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         not <| Map.containsKey recipientId account.InternalTransferRecipients
      then
         transitionErr RecipientNotFound
      else
         let recipient = account.InternalTransferRecipients[recipientId]

         if recipient.Status = RecipientRegistrationStatus.Closed then
            transitionErr RecipientDeactivated
         else
            map
               InternalRecipientDeactivated
               state
               (DeactivateInternalRecipientCommand.toEvent cmd)

   let depositTransfer
      (state: AccountWithEvents)
      (cmd: DepositTransferCommand)
      =
      if state.Info.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map TransferDeposited state <| (DepositTransferCommand.toEvent cmd)

   let closeAccount (state: AccountWithEvents) (cmd: CloseAccountCommand) =
      map AccountEvent.AccountClosed state (CloseAccountCommand.toEvent cmd)

   let nicknameRecipient
      (state: AccountWithEvents)
      (cmd: NicknameRecipientCommand)
      =
      let account = state.Info

      let recipientExists, recipientIsActive =
         match cmd.Data.RecipientAccountEnvironment with
         | RecipientAccountEnvironment.Internal ->
            match
               account.InternalTransferRecipients.TryFind cmd.Data.RecipientId
            with
            | None -> false, false
            | Some recipient ->
               true, recipient.Status <> RecipientRegistrationStatus.Closed
         | RecipientAccountEnvironment.Domestic ->
            match
               account.DomesticTransferRecipients.TryFind cmd.Data.RecipientId
            with
            | None -> false, false
            | Some recipient ->
               true, recipient.Status <> RecipientRegistrationStatus.Closed

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif not recipientExists then
         transitionErr RecipientNotFound
      else if not recipientIsActive then
         transitionErr RecipientDeactivated
      else
         map RecipientNicknamed state (NicknameRecipientCommand.toEvent cmd)

let stateTransition (state: AccountWithEvents) (command: AccountCommand) =
   match command with
   | CreateAccount cmd -> StateTransition.create state cmd
   | StartBillingCycle cmd -> StateTransition.startBillingcycle state cmd
   | DepositCash cmd -> StateTransition.deposit state cmd
   | Debit cmd -> StateTransition.debit state cmd
   | MaintenanceFee cmd -> StateTransition.maintenanceFee state cmd
   | SkipMaintenanceFee cmd -> StateTransition.skipMaintenanceFee state cmd
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

let empty: Account = {
   AccountId = AccountId System.Guid.Empty
   OrgId = OrgId System.Guid.Empty
   Name = ""
   Depository = AccountDepository.Checking
   Currency = Currency.USD
   Status = AccountStatus.InitialEmptyState
   Balance = 0m
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
   AccountNumber = AccountNumber <| System.Int64.Parse "123456789123456"
   RoutingNumber = RoutingNumber 123456789
}

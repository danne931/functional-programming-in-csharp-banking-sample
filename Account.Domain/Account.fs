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
      (account: Account)
      (cmd: InternalTransferCommand)
      =
      let info = cmd.Data.BaseInfo

      DateTime.isToday info.ScheduledDate
      && (dailyInternalTransferAccrued account.Events) + info.Amount > DailyInternalLimit

   let exceedsDailyDomesticTransferLimit
      (account: Account)
      (cmd: DomesticTransferCommand)
      =
      DateTime.isToday cmd.Data.ScheduledDate
      && (dailyDomesticTransferAccrued account.Events) + cmd.Data.Amount > DailyDomesticLimit

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
         Events = []
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
         }
      | InternalTransferPending e ->
         let balance = state.Balance - e.Data.BaseInfo.Amount

         {
            state with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebit state.MaintenanceFeeCriteria balance
               InProgressInternalTransfers =
                  Map.add e.CorrelationId e state.InProgressInternalTransfers
         }
      | InternalTransferApproved e -> {
         state with
            InProgressInternalTransfers =
               Map.remove e.CorrelationId state.InProgressInternalTransfers
        }
      | InternalTransferRejected e ->
         let balance = state.Balance + e.Data.BaseInfo.Amount

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
                  e.Data.BaseInfo.RecipientId
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
   let transitionErr (err: AccountStateTransitionError) =
      Error <| AccountStateTransitionError err

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
      if state.Status <> AccountStatus.InitialEmptyState then
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

   let debit (state: Account) (cmd: DebitCommand) =
      let input = cmd.Data

      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
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
      let input = cmd.Data.BaseInfo

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
   Events = []
   AccountNumber = AccountNumber <| System.Int64.Parse "123456789123456"
   RoutingNumber = RoutingNumber 123456789
}

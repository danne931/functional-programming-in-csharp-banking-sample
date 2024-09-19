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

   // TODO: Add monthly transfer accrued
   let dailyInternalTransferAccrued =
      transferAccrued DateTime.isToday (function
         | AccountEvent.InternalTransferWithinOrgPending e ->
            let info = e.Data.BaseInfo
            Some(info.ScheduledDate, info.Amount)
         | AccountEvent.InternalTransferWithinOrgRejected e ->
            let info = e.Data.BaseInfo
            Some(info.ScheduledDate, -info.Amount)
         | AccountEvent.InternalTransferBetweenOrgsPending e ->
            let info = e.Data.BaseInfo
            Some(info.ScheduledDate, info.Amount)
         | AccountEvent.InternalTransferBetweenOrgsRejected e ->
            let info = e.Data.BaseInfo
            Some(info.ScheduledDate, -info.Amount)
         | AccountEvent.PlatformPaymentPaid e ->
            Some(e.Timestamp, e.Data.BaseInfo.Amount)
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
      (amount: decimal)
      (scheduledDate: DateTime)
      =
      DateTime.isToday scheduledDate
      && (dailyInternalTransferAccrued evts) + amount > DailyInternalLimit

   let exceedsDailyDomesticTransferLimit
      (evts: AccountEvent list)
      (amount: decimal)
      (scheduledDate: DateTime)
      =
      DateTime.isToday scheduledDate
      && (dailyDomesticTransferAccrued evts) + amount > DailyDomesticLimit

let private applyInternalTransferPending
   (info: BaseInternalTransferInfo)
   (account: Account)
   =
   let balance = account.Balance - info.Amount

   {
      account with
         Balance = balance
         MaintenanceFeeCriteria =
            MaintenanceFee.fromDebit account.MaintenanceFeeCriteria balance
         InProgressInternalTransfers =
            Map.add
               info.TransferId
               {
                  TransferId = info.TransferId
                  Info = info
               }
               account.InProgressInternalTransfers
   }

let private applyInternalTransferApproved
   (info: BaseInternalTransferInfo)
   (account: Account)
   =
   {
      account with
         InProgressInternalTransfers =
            Map.remove info.TransferId account.InProgressInternalTransfers
   }

let private applyInternalTransferRejected
   (info: BaseInternalTransferInfo)
   (account: Account)
   =
   let balance = account.Balance + info.Amount

   {
      account with
         Balance = balance
         MaintenanceFeeCriteria =
            MaintenanceFee.fromDebitReversal
               account.MaintenanceFeeCriteria
               balance
         InProgressInternalTransfers =
            Map.remove info.TransferId account.InProgressInternalTransfers
   (*
         FailedInternalTransfers =
            Map.add
               e.CorrelationId
               (TransferEventToDomesticTransfer.fromRejection e)
               account.FailedDomesticTransfers
         *)
   }

let private applyTransferDeposit (account: Account) (amount: decimal) = {
   account with
      Balance = account.Balance + amount
      MaintenanceFeeCriteria =
         MaintenanceFee.fromDeposit account.MaintenanceFeeCriteria amount
}

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
         InProgressInternalTransfers = Map.empty
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
                  Map.remove info.TransferId account.FailedDomesticTransfers
               InProgressDomesticTransfers =
                  Map.add
                     info.TransferId
                     (TransferEventToDomesticTransfer.fromPending e)
                     account.InProgressDomesticTransfers
         }
      | DomesticTransferProgress e -> {
         account with
            InProgressDomesticTransfers =
               Map.change
                  e.Data.BaseInfo.TransferId
                  (Option.map (fun txn -> {
                     txn with
                        Status =
                           DomesticTransferProgress.InProgress
                              e.Data.InProgressInfo
                  }))
                  account.InProgressDomesticTransfers
        }
      | DomesticTransferApproved e -> {
         account with
            InProgressDomesticTransfers =
               Map.remove
                  e.Data.BaseInfo.TransferId
                  account.InProgressDomesticTransfers
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
            | DomesticTransferDeclinedReason.InvalidAccountInfo ->
               Some RecipientRegistrationStatus.InvalidAccount
            | DomesticTransferDeclinedReason.AccountClosed ->
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
                  Map.remove info.TransferId account.InProgressDomesticTransfers
               FailedDomesticTransfers =
                  Map.add
                     info.TransferId
                     (TransferEventToDomesticTransfer.fromRejection e)
                     account.FailedDomesticTransfers
               DomesticTransferRecipients = updatedRecipients
         }
      | InternalTransferWithinOrgPending e ->
         applyInternalTransferPending e.Data.BaseInfo account
      | InternalTransferWithinOrgApproved e ->
         applyInternalTransferApproved e.Data.BaseInfo account
      | InternalTransferWithinOrgRejected e ->
         applyInternalTransferRejected e.Data.BaseInfo account
      | InternalTransferBetweenOrgsPending e ->
         applyInternalTransferPending e.Data.BaseInfo account
      | InternalTransferBetweenOrgsApproved e ->
         applyInternalTransferApproved e.Data.BaseInfo account
      | InternalTransferBetweenOrgsRejected e ->
         applyInternalTransferRejected e.Data.BaseInfo account
      | InternalTransferWithinOrgDeposited e ->
         applyTransferDeposit account e.Data.BaseInfo.Amount
      | InternalTransferBetweenOrgsDeposited e ->
         applyTransferDeposit account e.Data.BaseInfo.Amount
      | PlatformPaymentRequested _ -> account
      | PlatformPaymentCancelled _ -> account
      | PlatformPaymentDeclined _ -> account
      | PlatformPaymentPaid e ->
         // If payment fulfilled with account funds then deduct the
         // payment amount from the account.
         match e.Data.PaymentMethod with
         | PaymentMethod.Platform _ ->
            let balance = account.Balance - e.Data.BaseInfo.Amount

            {
               account with
                  Balance = balance
                  MaintenanceFeeCriteria =
                     MaintenanceFee.fromDebit
                        account.MaintenanceFeeCriteria
                        balance
            }
         | PaymentMethod.ThirdParty _ -> account
      | PlatformPaymentDeposited e ->
         applyTransferDeposit account e.Data.BaseInfo.Amount
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
      | RecipientNicknamed e -> {
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
      (cmd: InternalTransferWithinOrgCommand)
      =
      let input = cmd.Data
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif account.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance account.Balance
      elif input.Recipient.OrgId <> account.OrgId then
         transitionErr TransferExpectedToOccurWithinOrg
      elif
         TransferLimits.exceedsDailyInternalTransferLimit
            state.Events
            input.Amount
            input.ScheduledDate
      then
         TransferLimits.DailyInternalLimit
         |> ExceededDailyInternalTransferLimit
         |> transitionErr
      else
         map
            InternalTransferWithinOrgPending
            state
            (InternalTransferWithinOrgCommand.toEvent cmd)

   let approveInternalTransfer
      (state: AccountWithEvents)
      (cmd: ApproveInternalTransferWithinOrgCommand)
      =
      let account = state.Info
      let transferId = cmd.Data.BaseInfo.TransferId

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind transferId account.InProgressInternalTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            InternalTransferWithinOrgApproved
            state
            (ApproveInternalTransferWithinOrgCommand.toEvent cmd)

   let rejectInternalTransfer
      (state: AccountWithEvents)
      (cmd: RejectInternalTransferWithinOrgCommand)
      =
      let account = state.Info
      let transferId = cmd.Data.BaseInfo.TransferId

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind transferId account.InProgressInternalTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            InternalTransferWithinOrgRejected
            state
            (RejectInternalTransferWithinOrgCommand.toEvent cmd)

   let internalTransferBetweenOrgs
      (state: AccountWithEvents)
      (cmd: InternalTransferBetweenOrgsCommand)
      =
      let input = cmd.Data
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif account.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance account.Balance
      elif
         TransferLimits.exceedsDailyInternalTransferLimit
            state.Events
            input.Amount
            input.ScheduledDate
      then
         TransferLimits.DailyInternalLimit
         |> ExceededDailyInternalTransferLimit
         |> transitionErr
      else
         map
            InternalTransferBetweenOrgsPending
            state
            (InternalTransferBetweenOrgsCommand.toEvent cmd)

   let approveInternalTransferBetweenOrgs
      (state: AccountWithEvents)
      (cmd: ApproveInternalTransferBetweenOrgsCommand)
      =
      let account = state.Info
      let transferId = cmd.Data.BaseInfo.TransferId

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind transferId account.InProgressInternalTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            InternalTransferBetweenOrgsApproved
            state
            (ApproveInternalTransferBetweenOrgsCommand.toEvent cmd)

   let rejectInternalTransferBetweenOrgs
      (state: AccountWithEvents)
      (cmd: RejectInternalTransferBetweenOrgsCommand)
      =
      let account = state.Info
      let transferId = cmd.Data.BaseInfo.TransferId

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind transferId account.InProgressInternalTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            InternalTransferBetweenOrgsRejected
            state
            (RejectInternalTransferBetweenOrgsCommand.toEvent cmd)

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
         TransferLimits.exceedsDailyDomesticTransferLimit
            state.Events
            input.Amount
            input.ScheduledDate
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
      let transferId = cmd.Data.BaseInfo.TransferId

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         let existing =
            Map.tryFind transferId account.InProgressDomesticTransfers

         match existing with
         | None ->
            map
               DomesticTransferProgress
               state
               (UpdateDomesticTransferProgressCommand.toEvent cmd)
         | Some txn ->
            let existingStatus = txn.Status

            if
               existingStatus = (DomesticTransferProgress.InProgress
                  cmd.Data.InProgressInfo)
            then
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
      let transferId = cmd.Data.BaseInfo.TransferId

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind transferId account.InProgressDomesticTransfers
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
      let transferId = cmd.Data.BaseInfo.TransferId

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind transferId account.InProgressDomesticTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map
            DomesticTransferRejected
            state
            (RejectDomesticTransferCommand.toEvent cmd)

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

   let depositTransferWithinOrg
      (state: AccountWithEvents)
      (cmd: DepositInternalTransferWithinOrgCommand)
      =
      if state.Info.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map InternalTransferWithinOrgDeposited state
         <| (DepositInternalTransferWithinOrgCommand.toEvent cmd)

   let depositTransferBetweenOrgs
      (state: AccountWithEvents)
      (cmd: DepositInternalTransferBetweenOrgsCommand)
      =
      if state.Info.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map InternalTransferBetweenOrgsDeposited state
         <| (DepositInternalTransferBetweenOrgsCommand.toEvent cmd)

   let platformPaymentRequested
      (state: AccountWithEvents)
      (cmd: RequestPlatformPaymentCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map
            PlatformPaymentRequested
            state
            (RequestPlatformPaymentCommand.toEvent cmd)

   let platformPaymentCancelled
      (state: AccountWithEvents)
      (cmd: CancelPlatformPaymentCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map
            PlatformPaymentCancelled
            state
            (CancelPlatformPaymentCommand.toEvent cmd)

   let platformPaymentDeclined
      (state: AccountWithEvents)
      (cmd: DeclinePlatformPaymentCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map
            PlatformPaymentDeclined
            state
            (DeclinePlatformPaymentCommand.toEvent cmd)

   let platformPaymentPaid
      (state: AccountWithEvents)
      (cmd: FulfillPlatformPaymentCommand)
      =
      let input = cmd.Data.RequestedPayment.BaseInfo
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif account.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance account.Balance
      elif
         TransferLimits.exceedsDailyInternalTransferLimit
            state.Events
            input.Amount
            cmd.Timestamp
      then
         TransferLimits.DailyInternalLimit
         |> ExceededDailyInternalTransferLimit
         |> transitionErr
      else
         map
            PlatformPaymentPaid
            state
            (FulfillPlatformPaymentCommand.toEvent cmd)

   let platformPaymentDeposited
      (state: AccountWithEvents)
      (cmd: DepositPlatformPaymentCommand)
      =
      let account = state.Info

      if account.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map
            PlatformPaymentDeposited
            state
            (DepositPlatformPaymentCommand.toEvent cmd)

   let closeAccount (state: AccountWithEvents) (cmd: CloseAccountCommand) =
      map AccountEvent.AccountClosed state (CloseAccountCommand.toEvent cmd)

   let nicknameRecipient
      (state: AccountWithEvents)
      (cmd: NicknameRecipientCommand)
      =
      let account = state.Info

      let recipientExists, recipientIsActive =
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
   | InternalTransferBetweenOrgs cmd ->
      StateTransition.internalTransferBetweenOrgs state cmd
   | ApproveInternalTransferBetweenOrgs cmd ->
      StateTransition.approveInternalTransferBetweenOrgs state cmd
   | RejectInternalTransferBetweenOrgs cmd ->
      StateTransition.rejectInternalTransferBetweenOrgs state cmd
   | DepositTransferWithinOrg cmd ->
      StateTransition.depositTransferWithinOrg state cmd
   | DepositTransferBetweenOrgs cmd ->
      StateTransition.depositTransferBetweenOrgs state cmd
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
   | RequestPlatformPayment cmd ->
      StateTransition.platformPaymentRequested state cmd
   | FulfillPlatformPayment cmd -> StateTransition.platformPaymentPaid state cmd
   | DepositPlatformPayment cmd ->
      StateTransition.platformPaymentDeposited state cmd
   | CancelPlatformPayment cmd ->
      StateTransition.platformPaymentCancelled state cmd
   | DeclinePlatformPayment cmd ->
      StateTransition.platformPaymentDeclined state cmd

let empty: Account = {
   AccountId = AccountId System.Guid.Empty
   OrgId = OrgId System.Guid.Empty
   Name = ""
   Depository = AccountDepository.Checking
   Currency = Currency.USD
   Status = AccountStatus.InitialEmptyState
   Balance = 0m
   LastBillingCycleDate = None
   InProgressInternalTransfers = Map.empty
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

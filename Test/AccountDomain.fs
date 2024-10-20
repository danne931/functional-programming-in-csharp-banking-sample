module AccountDomainTests

open System
open Expecto

open Bank.Account.Domain
open Bank.Transfer.Domain

let update = Account.stateTransition
let initState = Stub.accountStateWithEvents

[<Tests>]
let tests =
   testList "Account Domain State Transitions" [
      test "DepositCashCommand with invalid amount" {
         for amount in [ 0m; -1m ] do
            let command =
               AccountCommand.DepositCash <| Stub.command.depositCash amount

            let res = update initState command
            let err = Expect.wantError res "should be Result.Error"

            Expect.stringContains
               (string err)
               "ValidationError"
               "should be a validation error"
      }

      test "DepositCashCommand with valid amount" {
         for amount in [ 0.5m; 100m ] do
            let command = Stub.command.depositCash amount

            let res = update initState (AccountCommand.DepositCash command)

            let _, state = Expect.wantOk res "should be Result.Ok"

            Expect.equal
               state.Info.Balance
               (initState.Info.Balance + command.Data.Amount)
               "should result in account balanced incremented by command amount"
      }

      test "DepositCashCommand should recompute maintenance fee criteria" {
         let initState = {
            initState with
               Info.MaintenanceFeeCriteria.QualifyingDepositFound = false
               Info.MaintenanceFeeCriteria.DailyBalanceThreshold = false
         }

         let command =
            Stub.command.depositCash <| MaintenanceFee.QualifyingDeposit - 1m

         let res = update initState (AccountCommand.DepositCash command)
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.isFalse
            state.Info.MaintenanceFeeCriteria.QualifyingDepositFound
            "Deposit amount < qualifying amount should not skip maintenance fee"

         let command = Stub.command.depositCash MaintenanceFee.QualifyingDeposit
         let res = update initState (AccountCommand.DepositCash command)
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.isTrue
            state.Info.MaintenanceFeeCriteria.QualifyingDepositFound
            "Deposit amount >= qualifying amount should skip maintenance fee"
      }

      test "TransferDepositCommand should recompute maintenance fee criteria" {
         let initState = {
            initState with
               Info.MaintenanceFeeCriteria.QualifyingDepositFound = false
               Info.MaintenanceFeeCriteria.DailyBalanceThreshold = true
         }

         let command =
            Stub.command.depositTransfer
            <| MaintenanceFee.QualifyingDeposit - 10m

         let res =
            update initState <| AccountCommand.DepositTransferWithinOrg command

         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.isFalse
            state.Info.MaintenanceFeeCriteria.QualifyingDepositFound
            "Transfer deposit amount < qualifying amount should not skip maintenance fee"

         let command =
            Stub.command.depositTransfer MaintenanceFee.QualifyingDeposit

         let res =
            update initState (AccountCommand.DepositTransferWithinOrg command)

         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.isTrue
            state.Info.MaintenanceFeeCriteria.QualifyingDepositFound
            "Deposit amount >= qualifying amount should skip maintenance fee"
      }

      test "ApproveTransferCommand" {
         let transferAmount = 101m

         let res =
            update initState
            <| AccountCommand.InternalTransfer(
               Stub.command.internalTransfer transferAmount
            )

         let _, state = Expect.wantOk res "should be Result.Ok"
         let balanceAfterTransferRequest = state.Info.Balance

         Expect.equal
            balanceAfterTransferRequest
            (initState.Info.Balance - transferAmount)
            "A pending transfer decrements balance"

         let cmd =
            AccountCommand.ApproveInternalTransfer
               Stub.command.approveInternalTransfer

         let res = update state cmd
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.Balance
            balanceAfterTransferRequest
            "Approving a transfer does not decrement balance"
      }

      test "RejectTransferCommand updates balance" {
         let res =
            update initState
            <| AccountCommand.InternalTransfer(
               Stub.command.internalTransfer 101m
            )

         let _, state = Expect.wantOk res "should be Result.Ok"

         let command = Stub.command.rejectInternalTransfer 101m

         let res = update state <| AccountCommand.RejectInternalTransfer command

         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.Balance
            initState.Info.Balance
            "should add the transfer amount back to the balance"
      }

      test "RejectTransferCommand recomputes maintenance fee" {
         let initState = {
            initState with
               Info.Balance = MaintenanceFee.DailyBalanceThreshold + 100m
               Info.MaintenanceFeeCriteria.QualifyingDepositFound = false
               Info.MaintenanceFeeCriteria.DailyBalanceThreshold = true
         }

         let amount = 101m

         let command = Stub.command.internalTransfer amount
         let res = update initState <| AccountCommand.InternalTransfer command
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            false
            "the transfer should invalidate the daily balance threshold"

         let command = Stub.command.rejectInternalTransfer amount

         let res = update state <| AccountCommand.RejectInternalTransfer command

         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            true
            "should enable skipping maintenance fee if a rejected transfer
             brings the balance back up to the daily balance threshold"
      }

      test "TransferPending should recompute maintenance fee criteria" {
         let initState = {
            initState with
               Info.Balance = MaintenanceFee.DailyBalanceThreshold + 100m
               Info.MaintenanceFeeCriteria.QualifyingDepositFound = false
               Info.MaintenanceFeeCriteria.DailyBalanceThreshold = true
         }

         let command = Stub.command.internalTransfer 90m
         let res = update initState <| AccountCommand.InternalTransfer command
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.isTrue
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            "Balance is still above threshold."

         let command = Stub.command.internalTransfer 11m
         let res = update state <| AccountCommand.InternalTransfer command
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.isFalse
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            "Balance must meet threshold at all times to avoid maintenance fee"
      }

      test "Debit should recompute maintenance fee criteria" {
         let initState = {
            initState with
               Info.Balance = MaintenanceFee.DailyBalanceThreshold + 100m
               Info.MaintenanceFeeCriteria.QualifyingDepositFound = false
               Info.MaintenanceFeeCriteria.DailyBalanceThreshold = true
         }

         let command = Stub.command.debit 90m
         let res = update initState <| AccountCommand.Debit command
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.isTrue
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            "Balance is still above threshold."

         let command = Stub.command.debit 11m
         let res = update state <| AccountCommand.Debit command
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.isFalse
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            "Balance must meet threshold at all times to avoid maintenance fee"
      }

      test "DebitCommand with invalid amount" {
         for amount in [ 0m; -1m ] do
            let command = Stub.command.debit amount
            let res = update initState <| AccountCommand.Debit command
            let err = Expect.wantError res "should be Result.Error"

            Expect.stringContains
               (string err)
               "ValidationError"
               "should be a validation error"
      }

      test "DebitCommand with valid amount" {
         for amount in [ 25.5m; 100m ] do
            let command = Stub.command.debit amount
            let res = update initState <| AccountCommand.Debit command
            let _, state = Expect.wantOk res "should be Result.Ok"

            Expect.equal
               state.Info.Balance
               (initState.Info.Balance - command.Data.Amount)
               "should result in account balanced decremented by command amount"
      }

      (*
      test "DebitCommand against an account with locked card" {
         let state = {
            initState with
               Info.CardLocked = true
         }

         let command = Stub.command.debit 9.31m
         let res = update state <| AccountCommand.Debit command
         let err = Expect.wantError res "should be Result.Error"

         Expect.stringContains
            (string err)
            "AccountCardLocked"
            "should be an AccountCardLocked validation error"
      }
      *)

      test "DebitCommand against an account with insufficient balance" {
         let state = { initState with Info.Balance = 200m }

         let command = Stub.command.debit 300m
         let res = update state <| AccountCommand.Debit command
         let err = Expect.wantError res "should be Result.Error"

         Expect.stringContains
            (string err)
            "InsufficientBalance"
            "should be an InsufficientBalance validation error"
      }

      (* TODO: move to employee domain
      test "Debits accrue a daily debit balance" {
         let initState = {
            Stub.accountState with
               DailyDebitAccrued = 0m
         }

         let updates =
            List.fold
               (fun acc amount ->
                  let account, total = acc
                  let cmd = Stub.command.debit amount
                  let res = update account <| AccountCommand.Debit cmd

                  let _, newState = Expect.wantOk res "should be Result.Ok"

                  newState, total + amount)
               (initState, 0m)
               [ 21m; 33m; 109m ]

         let newState, debitTotal = updates

         Expect.equal
            newState.DailyDebitAccrued
            debitTotal
            "DailyDebitAccrued should accrue debits for the day"

         let command =
            Stub.command.debitWithDate 10m <| DateTime.UtcNow.AddDays(-1)

         let res = update newState <| AccountCommand.Debit command
         let _, newState = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            newState.DailyDebitAccrued
            debitTotal
            "DailyDebitAccrued should not accrue debits older than a day"
      }
      *)

      (* TODO: move to employee domain
      test "Setting a daily debit limit" {
         let state = {
            Stub.accountState with
               Balance = 1000m
               DailyDebitAccrued = 0m
               DailyDebitLimit = 0m
         }

         let command = Stub.command.limitDailyDebits 100m
         let res = update state <| AccountCommand.LimitDailyDebits command
         let _, account = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            account.DailyDebitLimit
            100m
            "Daily debit limit should be set"

         let command = Stub.command.debit 99m
         let res = update account <| AccountCommand.Debit command
         let _, account = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (account.Balance, account.DailyDebitAccrued)
            (state.Balance - 99m, 99m)
            "Daily debit should be accrued if under daily limit"

         let command = Stub.command.debit 2m
         let res = update account <| AccountCommand.Debit command
         let err = Expect.wantError res "should be Result.Error"

         Expect.stringContains
            (string err)
            "ExceededDailyDebit"
            "should be an ExceededDailyDebit validation error"
      }
      *)

      test "Internal transfers accrue a daily transfer balance" {
         let updates =
            List.fold
               (fun acc amount ->
                  let account, total = acc
                  let cmd = Stub.command.internalTransfer amount

                  let res =
                     update account <| AccountCommand.InternalTransfer cmd

                  let _, newState = Expect.wantOk res "should be Result.Ok"

                  newState, total + amount)
               (initState, 0m)
               [ 25m; 30m; 100m ]

         let newState, transferTotal = updates

         Expect.equal
            (Account.TransferLimits.dailyInternalTransferAccrued newState.Events)
            transferTotal
            "DailyInternalTransferAccrued should accrue transfers for the day"

         let command = Stub.command.internalTransfer 10m

         let command = {
            command with
               // TODO
               Data.ScheduledDateSeedOverride =
                  Some(DateTime.UtcNow.AddDays(-1))
         }

         let res = update newState <| AccountCommand.InternalTransfer command
         let _, newState = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Account.TransferLimits.dailyInternalTransferAccrued newState.Events)
            transferTotal
            "DailyInternalTransferAccrued should not accrue transfers older than a day"
      }

      test "Rejected internal transfers are removed from daily transfer accrual" {
         let transferAmount = 100m
         let command = Stub.command.internalTransfer transferAmount

         let res = update initState <| AccountCommand.InternalTransfer command

         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Account.TransferLimits.dailyInternalTransferAccrued state.Events)
            transferAmount
            "DailyInternalTransferAccrued should accrue transfers for the day"

         let cmd = Stub.command.rejectInternalTransfer transferAmount

         let res = update state <| AccountCommand.RejectInternalTransfer cmd
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Account.TransferLimits.dailyInternalTransferAccrued state.Events)
            0m
            "DailyInternalTransferAccrued should not include a rejected transfer"
      }

      test "Domestic transfers accrue a daily transfer balance" {
         let res =
            update
               initState
               (AccountCommand.RegisterDomesticTransferRecipient
                  Stub.command.registerDomesticRecipient)

         let (AccountEvent.DomesticTransferRecipient evt), state =
            Expect.wantOk res "should be Result.Ok"

         let recipientStubIdOverride = evt.Data.Recipient.AccountId

         let updates =
            List.fold
               (fun acc amount ->
                  let account, total = acc
                  let cmd = Stub.command.domesticTransfer amount

                  let cmd = {
                     cmd with
                        Data.Recipient.AccountId = recipientStubIdOverride
                  }

                  let res =
                     update account <| AccountCommand.DomesticTransfer cmd

                  let _, newState = Expect.wantOk res "should be Result.Ok"

                  newState, total + amount)
               (state, 0m)
               [ 25m; 30m; 100m ]

         let newState, transferTotal = updates

         Expect.equal
            (Account.TransferLimits.dailyDomesticTransferAccrued newState.Events)
            transferTotal
            "DailyDomesticTransferAccrued should accrue transfers for the day"

         let command = Stub.command.domesticTransfer 10m

         let command = {
            command with
               // TODO
               Data.ScheduledDateSeedOverride =
                  Some(DateTime.UtcNow.AddDays(-1))
               Data.Recipient.AccountId = recipientStubIdOverride
         }

         let res = update newState <| AccountCommand.DomesticTransfer command
         let _, newState = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Account.TransferLimits.dailyDomesticTransferAccrued newState.Events)
            transferTotal
            "DailyDomesticTransferAccrued should not accrue transfers older than a day"
      }

      test "Rejected domestic transfers are removed from daily transfer accrual" {
         let res =
            update
               initState
               (AccountCommand.RegisterDomesticTransferRecipient
                  Stub.command.registerDomesticRecipient)

         let (AccountEvent.DomesticTransferRecipient evt), state =
            Expect.wantOk res "should be Result.Ok"

         let transferAmount = 100m
         let command = Stub.command.domesticTransfer transferAmount

         let command = {
            command with
               Data.Recipient.AccountId = evt.Data.Recipient.AccountId
         }

         let res = update state <| AccountCommand.DomesticTransfer command

         let (AccountEvent.DomesticTransferPending transferPendingEvt), state =
            Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Account.TransferLimits.dailyDomesticTransferAccrued state.Events)
            transferAmount
            "DailyDomesticTransferAccrued should accrue transfers for the day"

         let cmd =
            RejectDomesticTransferCommand.create
               state.Info.CompositeId
               transferPendingEvt.CorrelationId
               transferPendingEvt.InitiatedById
               {
                  BaseInfo = transferPendingEvt.Data.BaseInfo
                  Reason = DomesticTransferDeclinedReason.AccountClosed
               }

         let res = update state <| AccountCommand.RejectDomesticTransfer cmd
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Account.TransferLimits.dailyDomesticTransferAccrued state.Events)
            0m
            "DailyDomesticTransferAccrued should not include a rejected transfer"
      }

      test
         "Maintenance fee command should recompute fee criteria for next
          billing cycle" {
         let initState = {
            initState with
               Info.Balance = MaintenanceFee.DailyBalanceThreshold
               Info.MaintenanceFeeCriteria.QualifyingDepositFound = false
               Info.MaintenanceFeeCriteria.DailyBalanceThreshold = true
         }

         let command = Stub.command.maintenanceFee
         let res = update initState <| AccountCommand.MaintenanceFee command
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.Balance
            (initState.Info.Balance - command.Data.Amount)
            "maintenance fee decrements balance by configured amount"

         Expect.equal
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            false
            "maintenance fee dropping balance below threshold invalidates
             daily balance threshold criteria for next billing cycle"

         let state = {
            initState with
               Info.Balance = MaintenanceFee.DailyBalanceThreshold + 100m
               Info.MaintenanceFeeCriteria.QualifyingDepositFound = false
               Info.MaintenanceFeeCriteria.DailyBalanceThreshold = false
         }

         let cmd = AccountCommand.MaintenanceFee Stub.command.maintenanceFee
         let res = update state cmd
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            true
            "daily balance threshold criteria for next billing cycle
             recomputed based on balance"
      }

      test
         "Skip maintenance fee command should recompute fee criteria for next
          billing cycle" {
         let initState = {
            initState with
               Info.Balance = MaintenanceFee.DailyBalanceThreshold
               Info.MaintenanceFeeCriteria.QualifyingDepositFound = false
               Info.MaintenanceFeeCriteria.DailyBalanceThreshold = true
         }

         let command = Stub.command.skipMaintenanceFee
         let res = update initState <| AccountCommand.SkipMaintenanceFee command
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.Balance
            initState.Info.Balance
            "skip maintenance - balance should be unchanged"

         Expect.equal
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            true
            "daily balance threshold criteria for next billing cycle
             recomputed based on balance"

         let state = {
            initState with
               Info.Balance = MaintenanceFee.DailyBalanceThreshold - 1m
               Info.MaintenanceFeeCriteria.QualifyingDepositFound = false
               Info.MaintenanceFeeCriteria.DailyBalanceThreshold = false
         }

         let cmd = Stub.command.skipMaintenanceFee
         let res = update state <| AccountCommand.SkipMaintenanceFee cmd
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.MaintenanceFeeCriteria.DailyBalanceThreshold
            false
            "daily balance threshold criteria for next billing cycle
             recomputed based on balance"
      }

      test "Commands against account with closed status" {
         let state = {
            initState with
               Info.Status = AccountStatus.Closed
         }

         let (commands: AccountCommand list) = [
            AccountCommand.DepositCash <| Stub.command.depositCash 10m
            AccountCommand.InternalTransfer <| Stub.command.internalTransfer 33m
            AccountCommand.DomesticTransfer <| Stub.command.domesticTransfer 31m
            AccountCommand.RegisterDomesticTransferRecipient
            <| Stub.command.registerDomesticRecipient
            AccountCommand.DepositTransferWithinOrg
            <| Stub.command.depositTransfer 931m
            AccountCommand.MaintenanceFee Stub.command.maintenanceFee
            AccountCommand.SkipMaintenanceFee Stub.command.skipMaintenanceFee
         ]

         for command in commands do
            let res = update state command
            let err = Expect.wantError res "should be Result.Error"

            Expect.stringContains
               (string err)
               "AccountNotActive"
               "should be an AccountNotActive StateTransition error"

         let cmd = Stub.command.debit 13m
         let res = update state <| AccountCommand.Debit cmd
         let err = Expect.wantError res "should be Result.Error"

         Expect.stringContains
            (string err)
            "AccountNotActive"
            "debit should result in an AccountNotActive StateTransition error"

         let res =
            update state
            <| AccountCommand.CreateAccount Stub.command.createAccount

         let err = Expect.wantError res "should be Result.Error"

         Expect.stringContains
            (string err)
            "AccountNotReadyToActivate"
            "createshould be an AccountNotReadyToActivate StateTransition error"

         let res =
            update state
            <| AccountCommand.CloseAccount Stub.command.closeAccount

         Expect.wantOk res "should be Result.Ok" |> ignore
      }

   // TODO
   (*
      test "Commands against account with locked card" {
         let state = {
            initState with
               Info.CardLocked = true
         }

         let (commands: AccountCommand list) = [
            AccountCommand.DepositCash <| Stub.command.depositCash 10m
            AccountCommand.LimitDailyDebits
            <| Stub.command.limitDailyDebits 101m
            AccountCommand.RegisterInternalTransferRecipient
               Stub.command.registerInternalRecipient
            AccountCommand.RegisterDomesticTransferRecipient
               Stub.command.registerDomesticRecipient
            AccountCommand.DepositTransfer <| Stub.command.depositTransfer 931m
            AccountCommand.LockCard Stub.command.lockCard
            AccountCommand.UnlockCard Stub.command.unlockCard
            AccountCommand.MaintenanceFee Stub.command.maintenanceFee
            AccountCommand.SkipMaintenanceFee Stub.command.skipMaintenanceFee
         ]

         for command in commands do
            let res = update state command
            Expect.wantOk res "should be Result.Ok" |> ignore

         let state = {
            state with
               InternalTransferRecipients =
                  Map [
                     Stub.internalRecipient.AccountId, Stub.internalRecipient
                  ]
               DomesticTransferRecipients =
                  Map [
                     Stub.domesticRecipient.AccountId, Stub.domesticRecipient
                  ]
         }

         let (commands: AccountCommand list) = [
            AccountCommand.InternalTransfer <| Stub.command.internalTransfer 33m
            AccountCommand.DomesticTransfer <| Stub.command.domesticTransfer 31m
         ]

         for command in commands do
            let res = update state command
            Expect.wantOk res "should be Result.Ok" |> ignore

         let cmd = Stub.command.debit 13m
         let res = update state <| AccountCommand.Debit cmd
         let err = Expect.wantError res "should be Result.Error"

         Expect.stringContains
            (string err)
            "AccountCardLocked"
            "debit should result in an AccountCardLocked StateTransition error"

         let res =
            update state
            <| AccountCommand.CloseAccount Stub.command.closeAccount

         Expect.wantOk res "should be Result.Ok" |> ignore
      }
      *)
   ]

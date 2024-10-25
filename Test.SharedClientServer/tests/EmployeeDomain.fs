module EmployeeDomainTests

open System

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open Bank.Employee.Domain
open Lib.SharedTypes

module Stub = EmployeeStub

let update = Employee.stateTransition
let initState = Stub.employeeStateWithEvents

let tests =
   testList "Employee Domain State Transitions" [
      test "Purchase request when card locked" {
         let command = Stub.command.lockCard
         let res = update initState (EmployeeCommand.LockCard command)
         let _, state = Expect.wantOk res "should be Result.Ok"

         let command = Stub.command.debit 9.31m
         let res = update state (EmployeeCommand.DebitRequest command)
         let err = Expect.wantError res "should be Result.Error"

         Expect.equal
            err
            (Err.EmployeeStateTransitionError
               EmployeeStateTransitionError.CardLocked)
            "should be an CardLocked validation error"
      }

      test "Daily purchases accrue" {
         let updates =
            List.fold
               (fun acc amount ->
                  let state, total = acc
                  let cmd = Stub.command.debit amount
                  let res = update state (EmployeeCommand.DebitRequest cmd)
                  let _, state = Expect.wantOk res "should be Result.Ok"

                  // Mock the approval sent from the account actor
                  let cmd = Stub.command.approveDebit
                  let cmd = { cmd with Data.Info.Amount = amount }
                  let res = update state (EmployeeCommand.ApproveDebit cmd)
                  let _, state = Expect.wantOk res "should be Result.Ok"

                  state, total + amount)
               (initState, 0m)
               [ 21m; 33m; 109m ]

         let state, debitTotal = updates

         Expect.equal
            (Employee.dailyPurchaseAccrued state.Events Stub.cardId)
            debitTotal
            "DailyDebitAccrued should accrue debits for the day"

         let command = Stub.command.debit 10m

         let res = update state (EmployeeCommand.DebitRequest command)
         let _, state = Expect.wantOk res "should be Result.Ok"

         // Mock the approval sent from the account actor
         let cmd = Stub.command.approveDebit

         let cmd = {
            cmd with
               // Ensure date is older than current day
               Data.Info.Date = DateTime.UtcNow.AddDays(-1)
         }

         let res = update state (EmployeeCommand.ApproveDebit cmd)
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Employee.dailyPurchaseAccrued state.Events Stub.cardId)
            debitTotal
            "DailyDebitAccrued should not accrue debits older than a day"
      }

      test "Setting a daily purchase limit" {
         let command = Stub.command.limitDailyDebits 100m
         let res = update initState (EmployeeCommand.LimitDailyDebits command)
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.Cards[command.Data.CardId].DailyPurchaseLimit
            100m
            "Daily purchase limit should be set"

         let purchaseAmt = 99m
         let command = Stub.command.debit purchaseAmt
         let res = update state (EmployeeCommand.DebitRequest command)
         let _, state = Expect.wantOk res "should be Result.Ok"

         // Mock the approval sent from the account actor
         let cmd = Stub.command.approveDebit

         let cmd = {
            cmd with
               Data.Info.Amount = purchaseAmt
         }

         let res = update state (EmployeeCommand.ApproveDebit cmd)
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Employee.dailyPurchaseAccrued state.Events command.Data.CardId)
            purchaseAmt
            "Daily purchase should be accrued if under daily limit"

         let command = Stub.command.debit 2m
         let res = update state (EmployeeCommand.DebitRequest command)
         let err = Expect.wantError res "should be Result.Error"

         Expect.stringContains
            (string err)
            "ExceededDailyDebit"
            "should be an ExceededDailyDebit validation error"
      }

      test "Monthly purchases accrue" {
         let currDate = DateTime.UtcNow

         let firstOfMonth =
            DateTime(currDate.Year, currDate.Month, 1).ToUniversalTime()

         let updates =
            List.fold
               (fun acc amount ->
                  let state, total = acc
                  let cmd = Stub.command.debit amount
                  let res = update state (EmployeeCommand.DebitRequest cmd)
                  let _, state = Expect.wantOk res "should be Result.Ok"

                  // Mock the approval sent from the account actor
                  let cmd = Stub.command.approveDebit

                  let cmd = {
                     cmd with
                        Data.Info.Amount = amount
                        Data.Info.Date = firstOfMonth
                  }

                  let res = update state (EmployeeCommand.ApproveDebit cmd)
                  let _, state = Expect.wantOk res "should be Result.Ok"

                  state, total + amount)
               (initState, 0m)
               [ 21m; 33m; 109m ]

         let state, debitTotal = updates

         Expect.equal
            (Employee.monthlyPurchaseAccrued state.Events Stub.cardId)
            debitTotal
            "MonthlyDebitAccrued should accrue debits for the month"

         let cmd = Stub.command.debit 10m
         let res = update state (EmployeeCommand.DebitRequest cmd)
         let _, state = Expect.wantOk res "should be Result.Ok"

         // Mock the approval sent from the account actor
         let cmd = Stub.command.approveDebit

         let cmd = {
            cmd with
               // Ensure date is older than current month
               Data.Info.Date = firstOfMonth.AddDays(-1)
         }

         let res = update state (EmployeeCommand.ApproveDebit cmd)
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Employee.monthlyPurchaseAccrued state.Events Stub.cardId)
            debitTotal
            "MonthlyDebitAccrued should not accrue debits older than a day"
      }

      test "Setting a monthly purchase limit" {
         let command = Stub.command.limitMonthlyDebits 100m
         let res = update initState (EmployeeCommand.LimitMonthlyDebits command)
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            state.Info.Cards[command.Data.CardId].MonthlyPurchaseLimit
            100m
            "Monthly purchase limit should be set"

         let purchaseAmt = 99m
         let command = Stub.command.debit purchaseAmt
         let res = update state (EmployeeCommand.DebitRequest command)
         let _, state = Expect.wantOk res "should be Result.Ok"

         // Mock the approval sent from the account actor
         let cmd = Stub.command.approveDebit

         let cmd = {
            cmd with
               Data.Info.Amount = purchaseAmt
         }

         let res = update state (EmployeeCommand.ApproveDebit cmd)
         let _, state = Expect.wantOk res "should be Result.Ok"

         Expect.equal
            (Employee.monthlyPurchaseAccrued state.Events command.Data.CardId)
            purchaseAmt
            "Monthly purchase should be accrued if under monthly limit"

         let command = Stub.command.debit 2m
         let res = update state (EmployeeCommand.DebitRequest command)
         let err = Expect.wantError res "should be Result.Error"

         Expect.stringContains
            (string err)
            "ExceededMonthlyDebit"
            "should be an ExceededMonthlyDebit validation error"
      }
   ]

namespace Bank.Account.Domain

open System

type private Currencies =
   | USD = 0
   | EUR = 1
   | THB = 2
   | VND = 3

module Validators =
   let accountCreate () =
      (fun (cmd: CreateAccountCommand) ->
         // TODO: handle varying currency codes
         if cmd.Balance < 100m then
            Error $"InvalidStartBalance {cmd.Balance}"
         elif not (Enum.IsDefined(typeof<Currencies>, cmd.Currency)) then
            Error $"Invalid currency {cmd.Currency}"
         else
            Ok cmd)

   let dailyDebitLimit () =
      (fun (cmd: LimitDailyDebitsCommand) ->
         if cmd.DebitLimit < 0m then
            Error "InvalidDailyDebitLimit"
         else
            Ok cmd)

   let deposit () =
      (fun (cmd: DepositCashCommand) ->
         if cmd.Amount < 1m then
            Error $"InvalidDepositAmount {cmd.Amount}"
         else
            Ok cmd)

   let debit () =
      (fun (cmd: DebitCommand) ->
         if cmd.Amount <= 0m then
            Error $"InvalidDebitAmount {cmd.Amount}"
         else
            Ok cmd)

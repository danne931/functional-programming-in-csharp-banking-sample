module MaintenanceFee

open System

open BankTypes

module Constants =
   let Fee = decimal 5
   let DailyBalanceThreshold = decimal 1500
   let QualifyingDeposit = decimal 250

type FeeCriteria = {
   mutable depositCriteria: bool
   mutable balanceCriteria: bool
   mutable account: Account.AccountState
}

let initFeeCriteria events = {
   depositCriteria = false
   balanceCriteria = true
   account = Account.initialAccountStateFromEventHistory events
}

let computeFeeCriteria (lookback: DateTime) (events: AccountEvent list) =
   List.fold
      (fun acc event ->
         acc.account <- Account.applyEvent acc.account event

         let (_, envelope) = Envelope.unwrap event

         if envelope.Timestamp < lookback then
            acc.balanceCriteria <-
               acc.account.Balance >= Constants.DailyBalanceThreshold

            acc
         else
            // Account balance must meet the balance threshold every day
            // for the last month in order to skip the monthly fee.
            acc.balanceCriteria <-
               acc.account.Balance >= Constants.DailyBalanceThreshold

            match event with
            | DepositedCash(e) ->
               acc.depositCriteria <-
                  e.Data.DepositedAmount >= Constants.QualifyingDeposit
            | _ -> ()

            acc)
      (initFeeCriteria events)
      (List.tail events)
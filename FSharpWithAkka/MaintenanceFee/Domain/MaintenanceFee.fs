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
   mutable account: AccountState
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
            // Account balance must meet the balance threshold EVERY day
            // for the last month in order to skip the monthly fee.
            match acc.account.Balance < Constants.DailyBalanceThreshold with
            | true -> acc.balanceCriteria <- false
            | false -> ()

            match event with
            | DepositedCash(e) ->
               acc.depositCriteria <-
                  e.Data.DepositedAmount >= Constants.QualifyingDeposit
            | _ -> ()

            acc)
      (initFeeCriteria events)
      events
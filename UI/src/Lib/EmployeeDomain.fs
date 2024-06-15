module Bank.Employee.UIDomain

open Bank.Employee.Domain
open Lib.SharedTypes

type EmployeesMaybe = Result<Employee list option, Err>

type EmployeeEventsMaybe = Result<EmployeeEvent list option, Err>

type EmployeeCardPairsMaybe = Result<Map<CardId, (Card * Employee)> option, Err>

(*
let employeeEventUIFriendly
   (employee: Employee)
   (txn: EmployeeEvent)
   : TransactionUIFriendly
   =
   let _, envelope = EmployeeEnvelope.unwrap txn

   let props = {
      DateNaked = envelope.Timestamp
      Date = dateUIFriendly envelope.Timestamp
      Name = envelope.EventName
      Origin = None
      AmountNaked = None
      Amount = None
      Sign = ""
      Info = None
      MoneyFlow = None
      Source = None
      Destination = None
   }

   let props =
      match txn with
      | DebitRequested e -> props

   {
      props with
         Amount =
            props.AmountNaked
            |> Option.map (fun amount -> props.Sign + Money.format amount)
   }
*)

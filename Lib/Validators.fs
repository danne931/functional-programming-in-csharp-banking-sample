module Lib.Validators

open System
open Validus

let amountValidator = Check.Decimal.greaterThan 0m

let nameValidator = Check.String.betweenLen 2 50

let dateNotDefaultValidator propName =
   let msg = sprintf "%s should not be missing"
   Check.WithMessage.DateTime.notEquals DateTime.MinValue msg propName

let transferRecipientIdValidator senderId =
   let msg = sprintf "%s should not equal sender id"
   Check.WithMessage.String.notEquals senderId msg "Recipient Id"

// TODO: ++ validation
let accountNumberValidator =
   Check.String.betweenLen 3 40 "Recipient account number"

// TODO: ++ validation
let routingNumberValidator =
   Check.required (Check.String.betweenLen 3 40) "Recipient routing number"

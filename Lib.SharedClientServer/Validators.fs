module Lib.Validators

open System
open Validus
open Lib.SharedTypes

let amountValidator = Check.Decimal.greaterThan 0m

let firstNameValidator = Check.String.betweenLen 2 50 "First name"
let lastNameValidator = Check.String.betweenLen 2 50 "Last name"

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

let originValidator = Check.String.greaterThanLen 2 "Origin"

let validationErrorsHumanFriendly
   (result: ValidationResult<'t>)
   : Result<'t, string>
   =
   result |> Result.mapError (Err.ValidationError >> _.HumanFriendly)

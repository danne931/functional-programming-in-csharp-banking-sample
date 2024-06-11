module Lib.Validators

open System
open Validus
open Validus.Operators
open Lib.SharedTypes

let parseInt: Validator<string, int> =
   fun field input ->
      try
         Int32.Parse input |> Ok
      with _ ->
         Error <| ValidationErrors.create field [ $"Invalid {field}" ]

let parseInt64: Validator<string, int64> =
   fun field input ->
      try
         Int64.Parse input |> Ok
      with _ ->
         Error <| ValidationErrors.create field [ $"Invalid {field}" ]

let parseDecimal: Validator<string, decimal> =
   fun field input ->
      try
         decimal input |> Ok
      with _ ->
         Error <| ValidationErrors.create field [ $"Invalid {field}" ]

let amountValidator = Check.Decimal.greaterThan 0m

let amountValidatorFromString = parseDecimal >=> amountValidator

let firstNameValidator = Check.String.betweenLen 2 50 "First name"
let lastNameValidator = Check.String.betweenLen 2 50 "Last name"

let dateNotDefaultValidator propName =
   let msg = sprintf "%s should not be missing"
   Check.WithMessage.DateTime.notEquals DateTime.MinValue msg propName

let transferRecipientIdValidator senderId =
   let msg = sprintf "%s should not equal sender id"
   Check.WithMessage.String.notEquals senderId msg "Recipient Id"

let accountNumberValidator: Validator<string, AccountNumber> =
   parseInt64 *|* string
   >=> Check.String.betweenLen 6 15 *|* (Int64.Parse >> AccountNumber)

let routingNumberValidator: Validator<string, RoutingNumber> =
   parseInt *|* string
   >=> Check.String.equalsLen 9 *|* (Int32.Parse >> RoutingNumber)

let originValidator = Check.String.greaterThanLen 2 "Origin"

let validationErrorsHumanFriendly
   (result: ValidationResult<'t>)
   : Result<'t, string>
   =
   result |> Result.mapError (Err.ValidationError >> _.HumanFriendly)

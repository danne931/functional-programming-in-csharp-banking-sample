module Lib.Validators

open System
open Validus
open Validus.Operators

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

let accountNameValidator = Check.String.betweenLen 2 50 "Account name"
let firstNameValidator = Check.String.betweenLen 2 50 "First name"
let lastNameValidator = Check.String.betweenLen 2 50 "Last name"

let dateNotDefaultValidator propName =
   let msg = sprintf "%s should not be missing"
   Check.WithMessage.DateTime.notEquals DateTime.MinValue msg propName

let dateInFutureValidator propName =
   let msg = sprintf "%s should be in the future"
   Check.WithMessage.DateTime.greaterThan DateTime.UtcNow msg propName

let transferRecipientIdValidator senderId =
   let msg = sprintf "%s should not equal sender id"
   Check.WithMessage.String.notEquals senderId msg "Recipient Id"

let originValidator = Check.String.greaterThanLen 2 "Origin"

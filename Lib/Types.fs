module Lib.Types

open System
open System.Net.Mail
open Validus

[<AbstractClass>]
type Command(entityId: Guid, correlationId: Guid) =
   member x.EntityId = entityId
   member x.Timestamp = DateTime.UtcNow

   member x.CorrelationId =
      if correlationId = Guid.Empty then
         Guid.NewGuid()
      else
         correlationId

type BankEvent<'E> = {
   EntityId: Guid
   Timestamp: DateTime
   Data: 'E
   CorrelationId: Guid
} with

   member x.EventName = typedefof<'E>.Name

type Envelope = {
   EntityId: Guid
   Timestamp: DateTime
   EventName: string
   CorrelationId: Guid
}

type StateTransitionError =
   | AccountNotReadyToActivate
   | AccountTransactionProcessingDisabled
   | AccountNotActive
   | AccountCardLocked
   | InsufficientBalance of decimal
   | ExceededDailyDebit of decimal
   | RecipientRegistrationRequired
   | RecipientAlreadyRegistered

type Err =
   | DatabaseError of exn
   | ValidationError of ValidationErrors
   | StateTransitionError of StateTransitionError

   override x.ToString() =
      match x with
      | DatabaseError e -> $"DatabaseError: %s{e.Message}"
      | ValidationError e -> $"ValidationError: {ValidationErrors.toList e}"
      | StateTransitionError e -> $"StateTransitionError: {e}"

type Currency =
   | USD
   | EUR
   | THB
   | VND

type Email = private {
   Email: string
} with

   override x.ToString() = x.Email

   static member ofString: Validator<string, Email> =
      fun field input ->
         let rule (x: string) =
            if String.IsNullOrEmpty x then
               false
            elif String.length x > 255 then
               false
            else
               try
                  (MailAddress x).Address = x
               with :? FormatException ->
                  false

         let message = sprintf "%s must be a valid email address"

         input
         |> Validator.create message rule field
         |> Result.map (fun v -> { Email = v })

   static member deserialize(email: string) : Email = { Email = email }

   static member empty = { Email = "" }

type Service =
   | DomesticTransfer
   | Email

type CircuitBreakerStatus =
   | Closed
   | HalfOpen
   | Open

type CircuitBreakerMessage = {
   Service: Service
   Status: CircuitBreakerStatus
}

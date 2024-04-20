module Lib.SharedTypes

open System
open Validus

let guid () = Guid.NewGuid()

type Command<'C> = {
   Id: Guid
   EntityId: Guid
   Timestamp: DateTime
   CorrelationId: Guid
   Data: 'C
}

module Command =
   let create<'t> (entityId: Guid) (correlationId: Guid) (data) : Command<'t> = {
      Id = guid ()
      EntityId = entityId
      Timestamp = DateTime.UtcNow
      CorrelationId = correlationId
      Data = data
   }

type BankEvent<'E> = {
   Id: Guid
   EntityId: Guid
   Timestamp: DateTime
   Data: 'E
   CorrelationId: Guid
} with
#if FABLE_COMPILER
   member x.EventName = ""
#else
   member x.EventName = typedefof<'E>.Name
#endif

module BankEvent =
   let create<'C, 'E> (command: Command<'C>) (evtData: 'E) : BankEvent<'E> = {
      Id = command.Id
      EntityId = command.EntityId
      CorrelationId = command.CorrelationId
      Timestamp = command.Timestamp
      Data = evtData
   }

type Envelope = {
   Id: Guid
   EntityId: Guid
   Timestamp: DateTime
   EventName: string
   CorrelationId: Guid
}

type StateTransitionError =
   | AccountNotReadyToActivate
   | AccountNotActive
   | AccountCardLocked
   | InsufficientBalance of decimal
   | ExceededDailyDebit of decimal
   | RecipientRegistrationRequired
   | RecipientAlreadyRegistered
   | RecipientAlreadyDeactivated
   | RecipientNotFound
   | SenderAlreadyRegistered
   | TransferProgressNoChange
   | TransferAlreadyProgressedToApprovedOrRejected

type Err =
   | DatabaseError of exn
   | ValidationError of ValidationErrors
   | StateTransitionError of StateTransitionError
   | SerializationError of string
   | InvalidStatusCodeError of serviceName: string * code: int
   | SignalRError of exn
   | NetworkError of exn

   override x.ToString() =
      match x with
      | DatabaseError e -> $"DatabaseError: %s{e.Message}"
      | ValidationError e -> $"ValidationError: {ValidationErrors.toList e}"
      | StateTransitionError e -> $"StateTransitionError: {e}"
      | InvalidStatusCodeError(service, code) ->
         $"InvalidStatusCodeError {service}: Invalid status code {code}"
      | SerializationError err -> $"SerializationError: {err}"
      | SignalRError e -> $"SignalRError: {e.Message}"
      | NetworkError e -> $"NetworkError: {e.Message}"

   member x.HumanFriendly =
      match x with
      | DatabaseError _ -> "Database Error"
      | SerializationError _ -> "Serialization Error"
      | SignalRError _ -> "SignalR Error"
      | NetworkError _ -> "Network Error"
      | InvalidStatusCodeError _ -> x.ToString()
      | ValidationError e ->
         e
         |> ValidationErrors.toList
         |> List.fold (fun acc errMsg -> acc + errMsg + ", ") ""
         |> fun str -> str.Remove(str.Length - 2)
      | StateTransitionError e ->
         match e with
         | StateTransitionError.AccountNotActive -> "Account Not Active"
         | StateTransitionError.AccountNotReadyToActivate ->
            "Account Not Ready to Activate"
         | StateTransitionError.AccountCardLocked -> "Account Card Locked"
         | StateTransitionError.ExceededDailyDebit limit ->
            $"Exceeded Daily Debit Limit ${limit}"
         | StateTransitionError.InsufficientBalance balance ->
            $"Insufficient Balance ${balance}"
         | StateTransitionError.SenderAlreadyRegistered ->
            "Sender Already Registered"
         | StateTransitionError.RecipientAlreadyRegistered ->
            "Recipient Already Registered"
         | StateTransitionError.RecipientAlreadyDeactivated ->
            "Recipient Already Deactivated"
         | StateTransitionError.RecipientNotFound -> "Recipient Not Found"
         | StateTransitionError.RecipientRegistrationRequired ->
            "Recipient Registration Required"
         | _ -> "Error Applying Transaction to Account"

[<RequireQualifiedAccess>]
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
#if FABLE_COMPILER
               true
#else
               try
                  (System.Net.Mail.MailAddress x).Address = x
               with :? FormatException ->
                  false
#endif

         let message = sprintf "%s must be a valid email address"

         input
         |> Validator.create message rule field
         |> Result.map (fun v -> { Email = v })

   static member deserialize(email: string) : Email = { Email = email }

   static member empty = { Email = "" }

[<RequireQualifiedAccess>]
type CircuitBreakerService =
   | DomesticTransfer
   | Email

[<RequireQualifiedAccess>]
type CircuitBreakerStatus =
   | Closed
   | HalfOpen
   | Open

type CircuitBreakerEvent = {
   Service: CircuitBreakerService
   Status: CircuitBreakerStatus
   Timestamp: DateTime
}

[<RequireQualifiedAccess>]
type CircuitBreakerMessage =
   | Lookup
   | CircuitBreaker of CircuitBreakerEvent

type CircuitBreakerActorState = {
   DomesticTransfer: CircuitBreakerStatus
   Email: CircuitBreakerStatus
}

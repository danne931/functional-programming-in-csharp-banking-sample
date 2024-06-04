module Lib.SharedTypes

open System
open Validus

module Guid =
   let parseOptional (id: string) =
      try
         Some <| Guid.Parse id
      with _ ->
         None

type OrgId =
   | OrgId of Guid

   override x.ToString() =
      let (OrgId id) = x
      string id

type EntityId =
   | EntityId of Guid

   override x.ToString() =
      let (EntityId id) = x
      string id

type CorrelationId =
   | CorrelationId of Guid

   override x.ToString() =
      let (CorrelationId id) = x
      string id

type EventId =
   | EventId of Guid

   override x.ToString() =
      let (EventId id) = x
      string id

type AccountId =
   | AccountId of Guid

   override x.ToString() =
      let (AccountId id) = x
      string id

module AccountId =
   let get (accountId: AccountId) : Guid =
      let (AccountId id) = accountId
      id

   let toEntityId (accountId: AccountId) : EntityId =
      let (AccountId id) = accountId
      EntityId id

   let fromEntityId (entityId: EntityId) : AccountId =
      let (EntityId id) = entityId
      AccountId id

module CorrelationId =
   let create () = CorrelationId <| Guid.NewGuid()

type Command<'C> = {
   Id: EventId
   EntityId: EntityId
   OrgId: OrgId
   Timestamp: DateTime
   CorrelationId: CorrelationId
   Data: 'C
}

module Command =
   let create<'t>
      (entityId: EntityId)
      (orgId: OrgId)
      (correlationId: CorrelationId)
      (data)
      : Command<'t>
      =
      {
         Id = EventId <| Guid.NewGuid()
         EntityId = entityId
         OrgId = orgId
         Timestamp = DateTime.UtcNow
         CorrelationId = correlationId
         Data = data
      }

type BankEvent<'E> = {
   Id: EventId
   EntityId: EntityId
   OrgId: OrgId
   Timestamp: DateTime
   Data: 'E
   CorrelationId: CorrelationId
} with
#if FABLE_COMPILER
   member x.EventName = ""
#else
   member x.EventName = typedefof<'E>.Name
#endif

module BankEvent =
   let create<'E> (command: Command<'E>) : BankEvent<'E> = {
      Id = command.Id
      EntityId = command.EntityId
      OrgId = command.OrgId
      CorrelationId = command.CorrelationId
      Timestamp = command.Timestamp
      Data = command.Data
   }

   let create2<'C, 'E> (command: Command<'C>) (evtData: 'E) : BankEvent<'E> = {
      Id = command.Id
      EntityId = command.EntityId
      OrgId = command.OrgId
      CorrelationId = command.CorrelationId
      Timestamp = command.Timestamp
      Data = evtData
   }

type Envelope = {
   Id: EventId
   EntityId: EntityId
   OrgId: OrgId
   Timestamp: DateTime
   EventName: string
   CorrelationId: CorrelationId
}

type StateTransitionError =
   | AccountNotReadyToActivate
   | AccountNotActive
   | AccountCardLocked
   | InsufficientBalance of decimal
   | ExceededDailyDebit of decimal
   | ExceededDailyInternalTransferLimit of decimal
   | ExceededDailyDomesticTransferLimit of decimal
   | RecipientRegistrationRequired
   | RecipientRegistered
   | RecipientDeactivated
   | RecipientNotFound
   | SenderRegistered
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
         | StateTransitionError.ExceededDailyInternalTransferLimit limit ->
            $"Exceeded Daily Internal Transfer Limit ${limit}"
         | StateTransitionError.ExceededDailyDomesticTransferLimit limit ->
            $"Exceeded Daily Internal Transfer Limit ${limit}"
         | StateTransitionError.InsufficientBalance balance ->
            $"Insufficient Balance ${balance}"
         | StateTransitionError.SenderRegistered -> "Sender Already Registered"
         | StateTransitionError.RecipientRegistered -> "Recipient Registered"
         | StateTransitionError.RecipientDeactivated -> "Recipient Deactivated"
         | StateTransitionError.RecipientNotFound -> "Recipient Not Found"
         | StateTransitionError.RecipientRegistrationRequired ->
            "Recipient Registration Required"
         | StateTransitionError.TransferAlreadyProgressedToApprovedOrRejected ->
            "Transfer already progressed to approved or rejected"
         | StateTransitionError.TransferProgressNoChange ->
            "Transfer progress no change"

[<RequireQualifiedAccess>]
type MoneyFlow =
   | None
   | In
   | Out

module MoneyFlow =
   let fromString (flow: string) : MoneyFlow option =
      match flow with
      | "In" -> Some MoneyFlow.In
      | "Out" -> Some MoneyFlow.Out
      | _ -> None

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

let ORG_ID_REMOVE_SOON =
   "ec3e94cc-eba1-4ff4-b3dc-55010ecf67b9" |> Guid.Parse |> OrgId

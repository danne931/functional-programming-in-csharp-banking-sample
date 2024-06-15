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

type EmployeeId =
   | EmployeeId of Guid

   override x.ToString() =
      let (EmployeeId id) = x
      string id

type CardId =
   | CardId of Guid

   override x.ToString() =
      let (CardId id) = x
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

module EmployeeId =
   let get (employeeId: EmployeeId) : Guid =
      let (EmployeeId id) = employeeId
      id

   let toEntityId (employeeId: EmployeeId) : EntityId =
      let (EmployeeId id) = employeeId
      EntityId id

   let fromEntityId (entityId: EntityId) : EmployeeId =
      let (EntityId id) = entityId
      EmployeeId id

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

type AccountStateTransitionError =
   | AccountNotReadyToActivate
   | AccountNotActive
   | InsufficientBalance of decimal
   | ExceededDailyInternalTransferLimit of decimal
   | ExceededDailyDomesticTransferLimit of decimal
   | RecipientRegistrationRequired
   | RecipientRegistered
   | RecipientDeactivated
   | RecipientNotFound
   | SenderRegistered
   | TransferProgressNoChange
   | TransferAlreadyProgressedToApprovedOrRejected

type EmployeeStateTransitionError =
   | EmployeeNotReadyToActivate
   | EmployeeNotActive
   | CardNotFound
   | CardLocked
   | ExceededDailyDebit of limit: decimal * accrued: decimal
   | DebitAlreadyProgressedToApprovedOrDeclined

type Err =
   | DatabaseError of exn
   | ValidationError of ValidationErrors
   | AccountStateTransitionError of AccountStateTransitionError
   | EmployeeStateTransitionError of EmployeeStateTransitionError
   | SerializationError of string
   | InvalidStatusCodeError of serviceName: string * code: int
   | SignalRError of exn
   | NetworkError of exn

   override x.ToString() =
      match x with
      | DatabaseError e -> $"DatabaseError: %s{e.Message}"
      | ValidationError e -> $"ValidationError: {ValidationErrors.toList e}"
      | AccountStateTransitionError e -> $"AccountStateTransitionError: {e}"
      | EmployeeStateTransitionError e -> $"EmployeeStateTransitionError: {e}"
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
      | AccountStateTransitionError e ->
         match e with
         | AccountStateTransitionError.AccountNotActive -> "Account Not Active"
         | AccountStateTransitionError.AccountNotReadyToActivate ->
            "Account Not Ready to Activate"
         | AccountStateTransitionError.ExceededDailyInternalTransferLimit limit ->
            $"Exceeded Daily Internal Transfer Limit ${limit}"
         | AccountStateTransitionError.ExceededDailyDomesticTransferLimit limit ->
            $"Exceeded Daily Internal Transfer Limit ${limit}"
         | AccountStateTransitionError.InsufficientBalance balance ->
            $"Insufficient Balance ${balance}"
         | AccountStateTransitionError.SenderRegistered ->
            "Sender Already Registered"
         | AccountStateTransitionError.RecipientRegistered ->
            "Recipient Registered"
         | AccountStateTransitionError.RecipientDeactivated ->
            "Recipient Deactivated"
         | AccountStateTransitionError.RecipientNotFound ->
            "Recipient Not Found"
         | AccountStateTransitionError.RecipientRegistrationRequired ->
            "Recipient Registration Required"
         | AccountStateTransitionError.TransferAlreadyProgressedToApprovedOrRejected ->
            "Transfer already progressed to approved or rejected"
         | AccountStateTransitionError.TransferProgressNoChange ->
            "Transfer progress no change"
      | EmployeeStateTransitionError e ->
         match e with
         | DebitAlreadyProgressedToApprovedOrDeclined ->
            "Not found in PendingPurchases. Likely already approved or declined."
         | EmployeeStateTransitionError.EmployeeNotActive ->
            "Employee Not Active"
         | EmployeeStateTransitionError.EmployeeNotReadyToActivate ->
            "Employee Not Ready to Activate"
         | EmployeeStateTransitionError.CardLocked -> "Card Locked"
         | EmployeeStateTransitionError.CardNotFound -> "Card Not Found"
         | EmployeeStateTransitionError.ExceededDailyDebit(limit, _) ->
            $"Exceeded Daily Debit Limit ${limit}"

[<RequireQualifiedAccess>]
type MoneyFlow =
   | In
   | Out

module MoneyFlow =
   let fromString (flow: string) : MoneyFlow option =
      match flow.ToLower() with
      | "in" -> Some MoneyFlow.In
      | "out" -> Some MoneyFlow.Out
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

type AccountNumber =
   | AccountNumber of int64

   override x.ToString() =
      let (AccountNumber num) = x
      string num

   member x.Last4 = x |> string |> (fun str -> str.Substring(str.Length - 4))

type RoutingNumber =
   | RoutingNumber of int

   override x.ToString() =
      let (RoutingNumber num) = x
      string num

type CommandProcessingResponse = {
   EventId: EventId
   CorrelationId: CorrelationId
   EntityId: EntityId
}

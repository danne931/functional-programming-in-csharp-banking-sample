module Lib.Types

open System

[<AbstractClass>]
type Command(entityId: Guid, correlationId: Guid) =
   member x.EntityId = entityId
   member x.Timestamp = DateTime.UtcNow

   member x.CorrelationId =
      if correlationId = Guid.Empty then
         Guid.NewGuid()
      else
         correlationId

type BankEvent<'E> =
   {
      EntityId: Guid
      Timestamp: DateTime
      Data: 'E
      CorrelationId: Guid
   }

   member x.EventName = typedefof<'E>.Name

type Envelope = {
   EntityId: Guid
   Timestamp: DateTime
   EventName: string
   CorrelationId: Guid
}

type Validator<'t> = 't -> Result<'t :> Command, string>

let PassValidation () = fun (cmd: 't :> Command) -> Ok cmd

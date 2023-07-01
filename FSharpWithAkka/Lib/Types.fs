module Lib.Types

open System
open System.Threading.Tasks

[<AbstractClass>]
type Command(entityId: Guid) =
   member x.EntityId = entityId
   member x.Timestamp = DateTime.UtcNow

type BankEvent<'E> =
   {
      EntityId: Guid
      Timestamp: DateTime
      Data: 'E
   }

   member x.EventName = typedefof<'E>.Name

type Envelope = {
   EntityId: Guid
   Timestamp: DateTime
   EventName: string
}

type Validator<'t> = 't -> Result<unit, string>
type AsyncValidator<'t> = 't -> Task<Result<unit, string>>

type Validators<'t when 't :> Command> =
   | Validator of Validator<'t>
   | AsyncValidator of AsyncValidator<'t>

let PassValidation () = (fun _ -> Ok()) |> Validator

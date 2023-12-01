[<RequireQualifiedAccess>]
module AccountSeederActor

open System
open Akka.Hosting
open Akkling

open Lib.Types
open Bank.Account.Domain
open ActorUtil

type SeedState = {
   Seeded: bool
   Commands: CreateAccountCommand list
}

let private initState = {
   Seeded = false
   Commands = [
      CreateAccountCommand(
         entityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4"),
         firstName = "Jelly",
         lastName = "Fish",
         balance = 1300,
         email = "jellyfish@gmail.com",
         currency = Currency.USD,
         correlationId = Guid.NewGuid()
      )
      CreateAccountCommand(
         entityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5"),
         firstName = "Star",
         lastName = "Fish",
         balance = 1000,
         email = "starfish@gmail.com",
         currency = Currency.USD,
         correlationId = Guid.NewGuid()
      )
   ]
}

let actorProps (getAccountRef: EntityRefGetter<AccountMessage>) =
   let handler (ctx: Actor<AccountSeederMessage>) =
      let logInfo = logInfo ctx

      let rec loop (state: SeedState) =
         function
         | SeedAccounts ->
            if state.Seeded then
               logInfo "Account seed already initialized"
               ignored ()
            else
               logInfo "Seed postgres with accounts"

               for command in state.Commands do
                  getAccountRef command.EntityId
                  <! (StateChange << CreateAccount) command

               become <| loop { state with Seeded = true }

      loop initState

   props <| actorOf2 handler

let get (registry: IActorRegistry) : IActorRef<AccountSeederMessage> =
   typed <| registry.Get<ActorMetadata.AccountSeederMarker>()

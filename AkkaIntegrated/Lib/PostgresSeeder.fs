module PostgresSeeder

open System
open Akka.Actor
open Akkling

open Lib.Types
open BankTypes
open Bank.Account.Domain

// Create accounts for local development
let seed (sys: ActorSystem) = task {
   let commands = [
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

   for command in commands do
      let ref = AccountActor.get sys command.EntityId
      let! (acct: AccountState option) = ref <? AccountMessage.Lookup

      if acct.IsNone then
         sys.Log.Log(
            Akka.Event.LogLevel.InfoLevel,
            null,
            $"Account doesn't exist.  Will create for {command.Email}"
         )

         ref <! AccountMessage.StateChange command
}

[<RequireQualifiedAccess>]
module AccountCoordinatorActor

open Microsoft.FSharp.Core.Option
open Akkling
open Akka.Actor

open BankTypes
open ActorUtil

let ActorName = "account_coordinator"
let getChild = getChildActorRef<AccountCoordinatorMessage, AccountMessage>

let start
   (system: ActorSystem)
   (persistence: AccountPersistence)
   (broadcast: AccountBroadcast)
   =
   let handler (mailbox: Actor<AccountCoordinatorMessage>) =
      function
      | InitAccount account ->
         ignored (AccountActor.start persistence broadcast mailbox account)
      | AccountCoordinatorMessage.StateChange cmd ->
         let accountRef = getChild mailbox (string cmd.EntityId)

         if isSome accountRef then
            accountRef.Value <! AccountMessage.StateChange cmd
            Ignore
         else
            let accountOpt = (persistence.loadAccount cmd.EntityId).Result

            if isSome accountOpt then
               let ref =
                  AccountActor.start
                     persistence
                     broadcast
                     mailbox
                     accountOpt.Value

               ref <! AccountMessage.StateChange cmd
               Ignore
            else
               printfn "Attempting to change state of a non-existent account."
               Unhandled
      | Delete id ->
         let accountRef = getChild mailbox (string id)

         if isSome accountRef then
            (retype accountRef.Value) <! PoisonPill.Instance

         Ignore

   spawn system ActorName (props (actorOf2 handler))

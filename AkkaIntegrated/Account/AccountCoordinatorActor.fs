[<RequireQualifiedAccess>]
module AccountCoordinatorActor

open Microsoft.FSharp.Core.Option
open Akkling
open Akka.Actor
open Akka.Routing

open BankTypes
open ActorUtil

let private actorName = ActorMetadata.accountCoordinator.Name

let getChild = getChildActorRef<AccountCoordinatorMessage, AccountMessage>

let start (system: ActorSystem) (broadcast: AccountBroadcast) =
   let handler (mailbox: Actor<AccountCoordinatorMessage>) =
      function
      | InitAccount account ->
         ignored (AccountActor.start broadcast mailbox account)
      | AccountCoordinatorMessage.StateChange cmd ->
         let accountRef = getChild mailbox (string cmd.EntityId)
         accountRef.Value <! AccountMessage.StateChange cmd
         Ignore

      (*
         if isSome accountRef then
            accountRef.Value <! AccountMessage.StateChange cmd
            Ignore
         else
            let accountOpt = (persistence.loadAccount cmd.EntityId).Result

            if isSome accountOpt then
               let ref =
                  AccountActor.start
                     broadcast
                     mailbox
                     accountOpt.Value

               ref <! AccountMessage.StateChange cmd
               Ignore
            else
               printfn
                  "%A: Attempting to change state of a non-existent account."
                  actorName

               Unhandled
         *)
      | Delete id ->
         let accountRef = getChild mailbox (string id)

         if isSome accountRef then
            (retype accountRef.Value) <! PoisonPill.Instance

         Ignore

   spawn
      system
      actorName
      {
         (props <| actorOf2 handler) with
            Router = Some FromConfig.Instance
      }

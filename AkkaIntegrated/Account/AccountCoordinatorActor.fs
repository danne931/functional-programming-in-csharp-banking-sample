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
      | AccountCoordinatorMessage.InitAccount cmd ->
         let aref = AccountActor.start broadcast mailbox cmd.EntityId
         aref <! AccountMessage.InitAccount cmd
         Ignore
      | AccountCoordinatorMessage.StateChange cmd ->
         let aref =
            string cmd.EntityId
            |> getChild mailbox
            |> Option.defaultWith (fun _ ->
               AccountActor.start broadcast mailbox cmd.EntityId)

         aref <! AccountMessage.StateChange cmd
         Ignore
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

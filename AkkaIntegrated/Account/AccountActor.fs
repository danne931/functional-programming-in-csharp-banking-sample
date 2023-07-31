[<RequireQualifiedAccess>]
module AccountActor

open System
open Akka.Actor
open Akkling
open Akkling.Persistence
open Akkling.Cluster.Sharding

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain

let private getInternalTransferActor =
   getChildActorRef<_, BankEvent<TransferPending>>

let private persist e = e |> Event |> box |> Persist

let start
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (system: ActorSystem)
   =
   let actorName = ActorMetadata.account.Name

   let handler (mailbox: Eventsourced<obj>) =
      let rec loop (account: AccountState) = actor {
         let path = mailbox.Self.Path
         let! msg = mailbox.Receive()

         return!
            match box msg with
            | Persisted mailbox e ->
               let (Event evt) = unbox e
               let newState = Account.applyEvent account evt
               broadcaster.broadcast (evt, newState) |> ignore

               match evt with
               | TransferPending evt -> mailbox.Self <! DispatchTransfer evt
               | _ -> ()

               loop newState
            | :? AccountMessage as msg ->
               match msg with
               | StartChildren ->
                  (*
                  MaintenanceFeeActor.start
                     //(fun _ -> TimeSpan.FromDays 30)
                     (fun _ -> TimeSpan.FromSeconds 40)
                     mailbox
                     accountId
                  *)
                  ignored ()
               | InitAccount cmd ->
                  let evt = cmd |> CreatedAccountEvent.create |> CreatedAccount
                  persist evt
               | Lookup ->
                  mailbox.Sender() <! account
                  ignored ()
               | StateChange cmd ->
                  let validation = Account.stateTransition account cmd

                  match validation with
                  | Error err ->
                     broadcaster.broadcastError err |> ignore
                     printfn "%A: validation fail %A" actorName err
                     ignored ()
                  | Ok(event, _) -> persist event
               | DispatchTransfer evt ->
                  let id = evt.EntityId

                  match evt.Data.Recipient.AccountEnvironment with
                  | RecipientAccountEnvironment.Internal ->
                     let aref =
                        (ActorMetadata.internalTransfer id).Name
                        |> getInternalTransferActor mailbox
                        |> Option.defaultWith (fun _ ->
                           InternalTransferRecipientActor.start
                              (mailbox :> Actor<_>)
                              persistence
                              id)

                     aref <! evt
                  | RecipientAccountEnvironment.Domestic ->
                     select mailbox ActorMetadata.domesticTransfer.Path.Value
                     <! (evt |> DomesticTransferRecipientActor.TransferPending)
                  | _ -> ()

                  ignored ()
               | Delete ->
                  printfn "Deleting message history: %A" path
                  DeleteMessages Int64.MaxValue
            // Event replay on actor start
            | :? AccountEvent as e when mailbox.IsRecovering() ->
               loop <| Account.applyEvent account e
            | LifecycleEvent _ -> ignored ()
            | :? Akka.Persistence.RecoveryCompleted -> ignored ()
            | :? Akka.Persistence.DeleteMessagesSuccess ->
               printfn "Deleted message history. Shutting down actor. %A" path
               passivate ()
            | :? Akka.Persistence.DeleteMessagesFailure as e ->
               printfn
                  "Failure to delete message history %A %A"
                  e.Cause.Message
                  path

               unhandled ()
            | :? PersistentLifecycleEvent as e ->
               match e with
               | ReplaySucceed -> ignored ()
               | ReplayFailed(exn, _) ->
                  failwith $"Persistence replay failed: {exn.Message}"
               | PersistRejected(exn, _, _)
               | PersistFailed(exn, _, _) ->
                  broadcaster.broadcastError exn.Message |> ignore
                  failwith $"Persistence failed: {exn.Message}"
            | msg ->
               printfn "Unknown message %A %A" msg mailbox.Self.Path
               unhandled ()
      }

      loop AccountState.empty

   AkklingExt.entityFactoryFor system actorName <| propsPersist handler

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
open Bank.User.Api

let private persist e =
   e |> Event |> box |> Persist :> Effect<_>

let start (broadcaster: AccountBroadcast) (system: ActorSystem) =
   let actorName = ActorMetadata.account.Name

   let handler (mailbox: Eventsourced<obj>) =
      let rec loop (accountOpt: AccountState option) = actor {
         let path = mailbox.Self.Path
         let! msg = mailbox.Receive()
         let account = Option.defaultValue AccountState.empty accountOpt

         return!
            match box msg with
            | Persisted mailbox e ->
               let (Event evt) = unbox e
               let newState = Account.applyEvent account evt
               broadcaster.broadcast (evt, newState) |> ignore

               match evt with
               | TransferPending e -> mailbox.Self <! DispatchTransfer e
               | _ -> ()

               loop (Some newState)
            | :? AccountMessage as msg ->
               match msg with
               | InitAccount cmd ->
                  let evt = cmd |> CreatedAccountEvent.create
                  // TODO: Consider creating a user actor & integrating an
                  //       auth workflow in the future.
                  //       Create the record & move along for now.
                  let (user: User.User) = {
                     FirstName = evt.Data.FirstName
                     LastName = evt.Data.LastName
                     AccountId = evt.EntityId
                     Email = evt.Data.Email
                  }

                  createUser(user).Wait()

                  mailbox.Self <! UserCreated evt
                  ignored ()
               | Lookup ->
                  mailbox.Sender() <! accountOpt
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
                  match evt.Data.Recipient.AccountEnvironment with
                  | RecipientAccountEnvironment.Internal ->
                     let aref =
                        InternalTransferRecipientActor.getOrStart mailbox

                     aref <! evt
                  | RecipientAccountEnvironment.Domestic ->
                     select mailbox ActorMetadata.domesticTransfer.Path.Value
                     <! (evt |> DomesticTransferRecipientActor.TransferPending)
                  | _ -> ()

                  ignored ()
               | UserCreated(evt: BankEvent<CreatedAccount>) ->
                  persist <| CreatedAccount evt
               | Delete ->
                  printfn "Deleting message history: %A" path
                  DeleteMessages Int64.MaxValue
            // Event replay on actor start
            | :? AccountEvent as e when mailbox.IsRecovering() ->
               loop <| Some(Account.applyEvent account e)
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

      loop None

   AkklingExt.entityFactoryFor system actorName <| propsPersist handler

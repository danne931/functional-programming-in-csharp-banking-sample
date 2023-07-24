[<RequireQualifiedAccess>]
module AccountActor

open System
open Akkling
open Akkling.Persistence

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain

let private getInternalTransferActor =
   getChildActorRef<AccountMessage, BankEvent<TransferPending>>

let private persist e =
   e |> Event |> Persist :> Effect<AccountMessage>

let start
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (mailbox: Actor<_>)
   (accountId: Guid)
   =
   let actorName = (ActorMetadata.account accountId).Name

   let handler (mailbox: Eventsourced<AccountMessage>) =
      let rec loop (account: AccountState) = actor {
         let! msg = mailbox.Receive()

         return!
            match msg with
            // Event replay on actor start
            | Event e -> loop <| Account.applyEvent account e
            // TODO: Listen on persistence lifecycle events such as PersistenceFail
            | Persisted mailbox (Event evt) ->
               let newState = Account.applyEvent account evt
               broadcaster.broadcast (evt, newState) |> ignore

               match evt with
               | TransferPending evt -> mailbox.Self <! DispatchTransfer evt
               | _ -> ()

               loop newState
            | StartChildren id ->
               MaintenanceFeeActor.start
                  persistence.loadAccountEvents
                  //(fun _ -> DateTime.UtcNow.AddDays -30)
                  //(fun _ -> TimeSpan.FromDays 30)
                  (fun _ -> DateTime.UtcNow.AddSeconds -90)
                  (fun _ -> TimeSpan.FromSeconds 90)
                  mailbox
                  id

               ignored ()
            | InitAccount cmd ->
               let evt = cmd |> CreatedAccountEvent.create |> CreatedAccount
               persist evt
            | Lookup _ ->
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
                           mailbox
                           persistence
                           id)

                  aref <! evt
               | RecipientAccountEnvironment.Domestic ->
                  select mailbox ActorMetadata.domesticTransfer.Path
                  <! (evt |> DomesticTransferRecipientActor.TransferPending)
               | _ -> ()

               ignored ()
      }

      loop AccountState.empty

   let aref = spawn mailbox actorName (propsPersist handler)
   aref <! StartChildren accountId
   aref

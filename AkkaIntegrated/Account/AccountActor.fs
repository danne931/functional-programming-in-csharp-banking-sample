[<RequireQualifiedAccess>]
module AccountActor

open System
open Akkling
open Akkling.Persistence

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Transfer.Domain

let private getInternalTransferActor =
   getChildActorRef<AccountMessage, BankEvent<TransferPending>>

let private persist e =
   e |> Event |> Persist :> Effect<AccountMessage>

let start
   (broadcaster: AccountBroadcast)
   (mailbox: Actor<_>)
   (initialState: AccountState)
   =
   let actorName = (ActorMetadata.account initialState.EntityId).Name

   let handler (mailbox: Eventsourced<AccountMessage>) =
      let rec loop (account: AccountState) = actor {
         let! msg = mailbox.Receive()

         return!
            match msg with
            | Persisted mailbox (Event evt) ->
               printfn "2. event persisted to journal %A" evt
               printfn "3. account: %A" account
               broadcaster.broadcast (evt, account) |> ignore

               match evt with
               | TransferPending evt when
                  evt.Data.Recipient.AccountEnvironment = RecipientAccountEnvironment.Internal
                  ->
                  let selection =
                     getInternalTransferActor
                        mailbox
                        (ActorMetadata.internalTransfer evt.EntityId).Name

                  let aref =
                     match selection with
                     | None _ ->
                        InternalTransferRecipientActor.start
                           mailbox
                           evt.EntityId
                     | Some aref -> aref

                  aref <! evt
               | TransferPending evt when
                  evt.Data.Recipient.AccountEnvironment = RecipientAccountEnvironment.Domestic
                  ->

                  select mailbox ActorMetadata.domesticTransfer.Path
                  <! (evt |> DomesticTransferRecipientActor.TransferPending)
               | _ -> ()

               loop <| Account.applyEvent account evt

            | StartChildren id ->
               (*
               MaintenanceFeeActor.start
                  persistence.loadAccountEvents
                  //(fun _ -> DateTime.UtcNow.AddDays -30)
                  //(fun _ -> TimeSpan.FromDays 30)
                  (fun _ -> DateTime.UtcNow.AddSeconds -90)
                  (fun _ -> TimeSpan.FromSeconds 90)
                  mailbox
                  id
               *)

               ignored ()
            | Lookup _ ->
               mailbox.Sender() <! account
               ignored ()
            | Event e ->
               printfn "1. event replay %A" e
               loop <| Account.applyEvent account e
            | StateChange cmd ->
               let validation = Account.stateTransition account cmd

               match validation with
               | Error err ->
                  broadcaster.broadcastError err |> ignore
                  printfn "%A: validation fail %A" actorName err
                  ignored ()
               | Ok(event, _) -> persist event
      }

      loop initialState

   let aref = spawn mailbox actorName (propsPersist handler)

   aref <! StartChildren initialState.EntityId
   aref

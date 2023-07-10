[<RequireQualifiedAccess>]
module AccountActor

open System
open Akkling

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Transfer.Domain

let getInternalTransferActor =
   getChildActorRef<AccountMessage, BankEvent<TransferPending>>

let start
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (mailbox: Actor<_>)
   (initialState: AccountState)
   =
   let actorName = (ActorMetadata.account initialState.EntityId).Name

   let rec handler (account: AccountState) (mailbox: Actor<AccountMessage>) =
      function
      | StartChildren id ->
         MaintenanceFeeActor.start
            persistence.loadAccountEvents
            //(fun _ -> DateTime.UtcNow.AddDays -30)
            //(fun _ -> TimeSpan.FromDays 30)
            (fun _ -> DateTime.UtcNow.AddMinutes -2)
            (fun _ -> TimeSpan.FromMinutes 2)
            mailbox
            id
            (Guid.NewGuid())

         ignored ()
      | Lookup _ ->
         mailbox.Sender() <! account
         ignored ()
      | StateChange cmd ->
         let validation = Account.stateTransition account cmd

         match validation with
         | Error(err) ->
            broadcaster.broadcastError err |> ignore
            printfn "%A: validation fail %A" actorName err
            become (handler account mailbox)
         | Ok((event, newState) as validationResult) ->
            try
               (event |> Envelope.unwrap |> persistence.save).Wait()
               broadcaster.broadcast validationResult |> ignore
            with err when true ->
               broadcaster.broadcastError err.Message |> ignore
               printfn "%A" err
               reraise ()

            match event with
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
                        persistence
                        evt.EntityId
                  | Some aref -> aref

               aref <! evt
            | TransferPending evt when
               evt.Data.Recipient.AccountEnvironment = RecipientAccountEnvironment.Domestic
               ->

               select mailbox ActorMetadata.domesticTransfer.Path
               <! (evt |> DomesticTransferRecipientActor.TransferPending)
            | _ -> ()

            become (handler newState mailbox)

   let aref =
      spawn mailbox actorName (initialState |> handler |> actorOf2 |> props)

   aref <! StartChildren initialState.EntityId
   aref

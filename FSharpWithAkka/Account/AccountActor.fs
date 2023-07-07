[<RequireQualifiedAccess>]
module AccountActor

open System
open Akkling

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Transfer.Domain

let transferActor
   (mailbox: Actor<AccountMessage>)
   (persistence: AccountPersistence)
   (evt: BankEvent<TransferPending>)
   =
   let (actorName, start) =
      match evt.Data.Recipient.AccountEnvironment with
      | RecipientAccountEnvironment.Internal ->
         (InternalTransferRecipientActor.ActorName,
          fun () -> InternalTransferRecipientActor.start mailbox persistence)
      | RecipientAccountEnvironment.Domestic ->
         (DomesticTransferRecipientActor.ActorName,
          fun () -> DomesticTransferRecipientActor.start mailbox)

   let get () =
      getChildActorRef<AccountMessage, BankEvent<TransferPending>>
         mailbox
         actorName

   (get, start)

let getTransferActor =
   getChildActorRef<AccountMessage, BankEvent<TransferPending>>

let start
   (persistence: AccountPersistence)
   (broadcaster: AccountBroadcast)
   (mailbox: Actor<_>)
   (initialState: AccountState)
   =
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
            printfn "AccountActor: validation fail %A" err
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
            | TransferPending evt ->
               let (getActor, startActor) =
                  transferActor mailbox persistence evt

               let aref =
                  match getActor () with
                  | None _ -> startActor ()
                  | Some aref -> aref

               aref <! evt
            | _ -> ()

            become (handler newState mailbox)

   let aref =
      spawn
         mailbox
         (string initialState.EntityId)
         (initialState |> handler |> actorOf2 |> props)

   aref <! StartChildren initialState.EntityId
   aref

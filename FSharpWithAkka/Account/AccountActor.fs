[<RequireQualifiedAccess>]
module AccountActor

open System
open Akkling

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Transfer.Domain

let getTransferActor =
   getChildActorRef<AccountMessage, BankEvent<DebitedTransfer>>

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
            | DebitedTransfer e ->
               let opt =
                  getTransferActor mailbox TransferRecipientActor.ActorName

               let aref =
                  match opt with
                  | None _ -> TransferRecipientActor.start mailbox
                  | Some aref -> aref

               aref <! e
            | _ -> ()

            become (handler newState mailbox)

   let aref =
      spawn
         mailbox
         (string initialState.EntityId)
         (initialState |> handler |> actorOf2 |> props)

   aref <! StartChildren initialState.EntityId
   aref

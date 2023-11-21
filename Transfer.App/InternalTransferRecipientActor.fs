[<RequireQualifiedAccess>]
module InternalTransferRecipientActor

open System
open Akkling

open Lib.Types
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain

module Command = TransferResponseToCommand

module private Msg =
   let approve = AccountMessage.StateChange << AccountCommand.ApproveTransfer
   let reject = AccountMessage.StateChange << AccountCommand.RejectTransfer
   let deposit = AccountMessage.StateChange << AccountCommand.DepositTransfer

let actorProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   : Props<BankEvent<TransferPending>>
   =
   let handler (ctx: Actor<_>) (evt: BankEvent<TransferPending>) = actor {
      let logWarning = logWarning ctx
      let recipient = evt.Data.Recipient
      let recipientId = Guid recipient.Identification

      let! (accountOpt: AccountState option) =
         getAccountRef recipientId <? AccountMessage.GetAccount

      match accountOpt with
      | None ->
         logWarning $"Transfer recipient not found {recipientId}"

         let msg = Msg.reject <| Command.reject evt "NoRecipientFound"
         getAccountRef evt.EntityId <! msg
      | Some account ->
         if account.Status = AccountStatus.Closed then
            logWarning $"Transfer recipient account closed"

            let msg = Msg.reject <| Command.reject evt "AccountClosed"
            getAccountRef evt.EntityId <! msg
         else
            let msg = Msg.approve <| Command.approve evt ""
            getAccountRef evt.EntityId <! msg

            let origin = string evt.EntityId

            let msg =
               Msg.deposit
               <| DepositTransferCommand(
                  recipientId,
                  evt.Data.DebitedAmount,
                  $"Account ({origin.Substring(origin.Length - 4)})",
                  // CorrelationId from sender transfer request traced to receiver's deposit.
                  evt.CorrelationId
               )

            getAccountRef recipientId <! msg
   }

   props <| actorOf2 handler

let getOrStart mailbox (getAccountRef: EntityRefGetter<AccountMessage>) =
   ActorMetadata.internalTransfer.Name
   |> getChildActorRef<_, BankEvent<TransferPending>> mailbox
   |> Option.defaultWith (fun _ ->
      spawn mailbox ActorMetadata.internalTransfer.Name
      <| actorProps getAccountRef)

[<RequireQualifiedAccess>]
module InternalTransferRecipientActor

open System
open Akkling

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Transfer.Domain

module Command = TransferResponseToCommand

let actorProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   : Props<BankEvent<TransferPending>>
   =
   let handler (ctx: Actor<_>) (evt: BankEvent<TransferPending>) = actor {
      let logWarning = logWarning ctx
      let recipient = evt.Data.Recipient
      let recipientId = Guid recipient.Identification

      let! (accountOpt: AccountState option) =
         getAccountRef recipientId <? Lookup

      match accountOpt with
      | None ->
         logWarning $"Transfer recipient not found {recipientId}"

         let msg =
            AccountMessage.StateChange <| Command.reject evt "NoRecipientFound"

         getAccountRef evt.EntityId <! msg
      | Some account ->
         if account.Status = AccountStatus.Closed then
            logWarning $"Transfer recipient account closed"

            let msg =
               AccountMessage.StateChange <| Command.reject evt "AccountClosed"

            getAccountRef evt.EntityId <! msg
         else
            let msg = AccountMessage.StateChange <| Command.approve evt ""
            getAccountRef evt.EntityId <! msg

            let origin = string evt.EntityId

            let msg =
               AccountMessage.StateChange
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

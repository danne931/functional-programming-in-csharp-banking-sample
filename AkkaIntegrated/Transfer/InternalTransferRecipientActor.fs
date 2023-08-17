[<RequireQualifiedAccess>]
module InternalTransferRecipientActor

open System
open Akkling

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain

module Command = TransferResponseToCommand

let start (mailbox: Actor<_>) : IActorRef<BankEvent<TransferPending>> =
   let fac = AccountActorFac mailbox.System

   let handler (ctx: Actor<_>) (evt: BankEvent<TransferPending>) = actor {
      let logWarning = logWarning ctx
      let recipient = evt.Data.Recipient
      let recipientId = Guid recipient.Identification
      let! accountOpt = fac.ask<AccountState option> recipientId Lookup

      match accountOpt with
      | None ->
         logWarning $"Transfer recipient not found {recipientId}"

         let msg =
            AccountMessage.StateChange <| Command.reject evt "NoRecipientFound"

         fac.tell evt.EntityId msg
      | Some account ->
         if account.Status = AccountStatus.Closed then
            logWarning $"Transfer recipient account closed"

            let msg =
               AccountMessage.StateChange <| Command.reject evt "AccountClosed"

            fac.tell evt.EntityId msg
         else
            let msg = AccountMessage.StateChange <| Command.approve evt ""
            fac.tell evt.EntityId msg

            let origin = string evt.EntityId

            let msg =
               AccountMessage.StateChange
               <| DepositCashCommand(
                  recipientId,
                  evt.Data.DebitedAmount,
                  $"Account ({origin.Substring(origin.Length - 4)})",
                  // CorrelationId from sender transfer request traced to receiver's deposit.
                  evt.CorrelationId
               )

            fac.tell recipientId msg
   }

   spawn mailbox ActorMetadata.internalTransfer.Name (props (actorOf2 handler))

let getOrStart mailbox =
   ActorMetadata.internalTransfer.Name
   |> getChildActorRef<_, BankEvent<TransferPending>> mailbox
   |> Option.defaultWith (fun _ -> start mailbox)

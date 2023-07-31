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

let private issueTransferToRecipient
   (accountFac: AccountActorFac)
   (persistence: AccountPersistence)
   (evt: BankEvent<TransferPending>)
   =
   task {
      let recipient = evt.Data.Recipient

      match! persistence.loadAccount (Guid recipient.Identification) with
      | None ->
         let msg =
            AccountMessage.StateChange <| Command.reject evt "NoRecipientFound"

         accountFac.tell evt.EntityId msg
      | Some account ->
         if account.Status = AccountStatus.Closed then
            let msg =
               AccountMessage.StateChange <| Command.reject evt "AccountClosed"

            accountFac.tell evt.EntityId msg
         else
            let msg = AccountMessage.StateChange <| Command.approve evt ""
            accountFac.tell evt.EntityId msg

            let origin = string evt.EntityId
            let recipientId = Guid recipient.Identification

            let msg =
               AccountMessage.StateChange
               <| DepositCashCommand(
                  recipientId,
                  evt.Data.DebitedAmount,
                  $"Account ({origin.Substring(origin.Length - 4)})",
                  // CorrelationId from sender transfer request traced to receiver's deposit.
                  evt.CorrelationId
               )

            accountFac.tell recipientId msg
   }

let start
   (mailbox: Actor<_>)
   (persistence: AccountPersistence)
   (accountId: Guid)
   =
   let fac = AccountActorFac mailbox.System

   let handler _ evt =
      (issueTransferToRecipient fac persistence evt).Wait()
      Ignore

   spawn
      mailbox
      (ActorMetadata.internalTransfer accountId).Name
      (props (actorOf2 handler))

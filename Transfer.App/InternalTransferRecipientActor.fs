[<RequireQualifiedAccess>]
module InternalTransferRecipientActor

open System
open Akkling
open Akkling.Cluster.Sharding

open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes

let actorProps
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   : Props<InternalTransferMessage>
   =
   let handler (ctx: Actor<_>) (msg: InternalTransferMessage) = actor {
      let logWarning = logWarning ctx

      match msg with
      | InternalTransferMessage.ConfirmRecipient(sender, recipient) ->
         let recipientId = recipient.Identification |> Guid.Parse |> AccountId
         let senderAccountRef = getAccountRef sender.AccountId
         let recipientAccountRef = getAccountRef recipientId

         let! (recipientAccountOpt: Account option) =
            recipientAccountRef <? AccountMessage.GetAccount

         match recipientAccountOpt with
         | None ->
            logWarning $"Transfer recipient not found {recipientId}"

            let msg =
               DeactivateInternalRecipientCommand.create sender {
                  RecipientName = recipient.Name
                  RecipientId = recipientId
               }
               |> AccountCommand.DeactivateInternalRecipient
               |> AccountMessage.StateChange

            senderAccountRef <! msg
         | Some recipientAccount ->
            if recipientAccount.Status = AccountStatus.Closed then
               logWarning $"Transfer recipient account closed"

               let msg =
                  DeactivateInternalRecipientCommand.create sender {
                     RecipientId = recipientId
                     RecipientName = recipient.Name
                  }
                  |> AccountCommand.DeactivateInternalRecipient
                  |> AccountMessage.StateChange

               senderAccountRef <! msg
            else
               let msg =
                  RegisterInternalSenderCommand.create
                     recipientAccount.CompositeId
                     { Sender = sender }
                  |> AccountCommand.RegisterInternalSender
                  |> AccountMessage.StateChange

               recipientAccountRef <! msg
      | InternalTransferMessage.TransferRequest txn ->
         let recipient = txn.Recipient
         let recipientId = recipient.Identification |> Guid.Parse |> AccountId

         let senderAccountRef = getAccountRef txn.SenderAccountId
         let recipientAccountRef = getAccountRef recipientId

         let! (recipientAccountOpt: Account option) =
            recipientAccountRef <? AccountMessage.GetAccount

         match recipientAccountOpt with
         | None ->
            logWarning $"Transfer recipient not found {recipientId}"

            let msg =
               TransferTransactionToCommand.reject
                  txn
                  TransferDeclinedReason.InvalidAccountInfo
               |> AccountCommand.RejectTransfer
               |> AccountMessage.StateChange

            senderAccountRef <! msg
         | Some recipientAccount ->
            if recipientAccount.Status = AccountStatus.Closed then
               logWarning $"Transfer recipient account closed"

               let msg =
                  TransferTransactionToCommand.reject
                     txn
                     TransferDeclinedReason.AccountClosed
                  |> AccountCommand.RejectTransfer
                  |> AccountMessage.StateChange

               senderAccountRef <! msg
            else
               let msg =
                  TransferTransactionToCommand.approve txn
                  |> AccountCommand.ApproveTransfer
                  |> AccountMessage.StateChange

               senderAccountRef <! msg

               let origin = string txn.SenderAccountId

               // CorrelationId (here as txn.TransactionId) from sender
               // transfer request traced to receiver's deposit.
               let msg =
                  DepositTransferCommand.create
                     recipientAccount.CompositeId
                     txn.TransferId
                     {
                        Amount = txn.Amount
                        Origin =
                           $"Account ({origin.Substring(origin.Length - 4)})"
                     }
                  |> AccountCommand.DepositTransfer
                  |> AccountMessage.StateChange

               recipientAccountRef <! msg
   }

   props <| actorOf2 handler

let getOrStart
   mailbox
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   =
   ActorMetadata.internalTransfer.Name
   |> getChildActorRef<_, InternalTransferMessage> mailbox
   |> Option.defaultWith (fun _ ->
      spawn mailbox ActorMetadata.internalTransfer.Name
      <| actorProps getAccountRef)

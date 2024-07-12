[<RequireQualifiedAccess>]
module InternalTransferRecipientActor

open Akkling
open Akkling.Cluster.Sharding

open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes

type InternalRecipientConfirmation = {
   Sender: InternalTransferSender
   Recipient: InternalTransferRecipient
   InitiatedBy: InitiatedById
}

[<RequireQualifiedAccess>]
type InternalTransferMessage =
   | TransferRequest of BankEvent<InternalTransferPending>
   | ConfirmRecipient of InternalRecipientConfirmation

let actorProps
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   : Props<InternalTransferMessage>
   =
   let handler (ctx: Actor<_>) (msg: InternalTransferMessage) = actor {
      let logWarning = logWarning ctx

      match msg with
      | InternalTransferMessage.ConfirmRecipient(conf) ->
         let sender = conf.Sender
         let recipient = conf.Recipient
         let senderAccountRef = getAccountRef sender.AccountId
         let recipientAccountRef = getAccountRef recipient.AccountId

         let! (recipientAccountOpt: Account option) =
            recipientAccountRef <? AccountMessage.GetAccount

         match recipientAccountOpt with
         | None ->
            logWarning $"Transfer recipient not found {recipient.AccountId}"

            let msg =
               DeactivateInternalRecipientCommand.create sender conf.InitiatedBy {
                  RecipientName = recipient.Name
                  RecipientId = recipient.AccountId
               }
               |> AccountCommand.DeactivateInternalRecipient
               |> AccountMessage.StateChange

            senderAccountRef <! msg
         | Some recipientAccount ->
            if recipientAccount.Status = AccountStatus.Closed then
               logWarning $"Transfer recipient account closed"

               let msg =
                  DeactivateInternalRecipientCommand.create
                     sender
                     conf.InitiatedBy
                     {
                        RecipientId = recipient.AccountId
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
      | InternalTransferMessage.TransferRequest evt ->
         let recipientId = evt.Data.RecipientId
         let senderId = AccountId.fromEntityId evt.EntityId
         let recipientAccountRef = getAccountRef recipientId
         let senderAccountRef = getAccountRef senderId
         let amount = evt.Data.Amount

         let! (recipientAccountOpt: Account option) =
            recipientAccountRef <? AccountMessage.GetAccount

         let declineTransferMsg (reason: TransferDeclinedReason) =
            RejectInternalTransferCommand.create
               (senderId, evt.OrgId)
               evt.CorrelationId
               evt.InitiatedById
               {
                  RecipientId = recipientId
                  Amount = amount
                  Reason = reason
                  ScheduledDate = evt.Data.ScheduledDate
               }
            |> AccountCommand.RejectInternalTransfer
            |> AccountMessage.StateChange

         match recipientAccountOpt with
         | None ->
            logWarning $"Transfer recipient not found {recipientId}"

            let msg =
               declineTransferMsg TransferDeclinedReason.InvalidAccountInfo

            senderAccountRef <! msg
         | Some recipientAccount ->
            if recipientAccount.Status = AccountStatus.Closed then
               logWarning $"Transfer recipient account closed"

               let msg = declineTransferMsg TransferDeclinedReason.AccountClosed

               senderAccountRef <! msg
            else
               let msg =
                  ApproveInternalTransferCommand.create
                     (senderId, evt.OrgId)
                     evt.CorrelationId
                     evt.InitiatedById
                     {
                        RecipientId = recipientId
                        Amount = amount
                        ScheduledDate = evt.Data.ScheduledDate
                     }
                  |> AccountCommand.ApproveInternalTransfer
                  |> AccountMessage.StateChange

               senderAccountRef <! msg

               // CorrelationId (here as txn.TransactionId) from sender
               // transfer request traced to receiver's deposit.
               let msg =
                  DepositTransferCommand.create
                     recipientAccount.CompositeId
                     evt.CorrelationId
                     evt.InitiatedById
                     { Amount = amount; Origin = senderId }
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

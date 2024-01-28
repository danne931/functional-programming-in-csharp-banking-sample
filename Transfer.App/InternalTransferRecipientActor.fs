[<RequireQualifiedAccess>]
module InternalTransferRecipientActor

open System
open Akkling

open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain

module Command = TransferTransactionToCommand

module private Msg =
   let approve = AccountMessage.StateChange << AccountCommand.ApproveTransfer
   let reject = AccountMessage.StateChange << AccountCommand.RejectTransfer
   let deposit = AccountMessage.StateChange << AccountCommand.DepositTransfer

let actorProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   : Props<TransferTransaction>
   =
   let handler (ctx: Actor<_>) (txn: TransferTransaction) = actor {
      let logWarning = logWarning ctx
      let recipient = txn.Recipient
      let recipientId = Guid recipient.Identification

      let senderAccountRef = getAccountRef txn.SenderAccountId
      let recipientAccountRef = getAccountRef recipientId

      let! (accountOpt: AccountState option) =
         recipientAccountRef <? AccountMessage.GetAccount

      match accountOpt with
      | None ->
         logWarning $"Transfer recipient not found {recipientId}"

         let msg = Msg.reject <| Command.reject txn "NoRecipientFound"
         recipientAccountRef <! msg
      | Some account ->
         if account.Status = AccountStatus.Closed then
            logWarning $"Transfer recipient account closed"

            let msg = Msg.reject <| Command.reject txn "AccountClosed"
            senderAccountRef <! msg
         else
            let msg = Msg.approve <| Command.approve txn
            senderAccountRef <! msg

            let origin = string txn.SenderAccountId

            let msg =
               Msg.deposit
               <| DepositTransferCommand(
                  recipientId,
                  txn.Amount,
                  $"Account ({origin.Substring(origin.Length - 4)})",
                  // CorrelationId from sender transfer request traced to receiver's deposit.
                  txn.TransactionId
               )

            recipientAccountRef <! msg
   }

   props <| actorOf2 handler

let getOrStart mailbox (getAccountRef: EntityRefGetter<AccountMessage>) =
   ActorMetadata.internalTransfer.Name
   |> getChildActorRef<_, TransferTransaction> mailbox
   |> Option.defaultWith (fun _ ->
      spawn mailbox ActorMetadata.internalTransfer.Name
      <| actorProps getAccountRef)

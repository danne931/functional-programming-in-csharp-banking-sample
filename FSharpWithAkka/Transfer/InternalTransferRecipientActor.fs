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

let ActorName = "internal_transfer_recipient"

let private issueTransferToRecipient
   (mailbox: Actor<BankEvent<TransferPending>>)
   (persistence: AccountPersistence)
   (evt: BankEvent<TransferPending>)
   =
   task {
      let recipient = evt.Data.Recipient

      match! persistence.loadAccount (Guid recipient.Identification) with
      | None ->
         mailbox.Parent<AccountMessage>()
         <! AccountMessage.StateChange(
            TransferResponseToCommand.reject evt "NoRecepientFound"
         )
      | Some account ->
         if account.Status = AccountStatus.Closed then
            mailbox.Parent<AccountMessage>()
            <! AccountMessage.StateChange(Command.reject evt "AccountClosed")
         else
            mailbox.Parent<AccountMessage>()
            <! AccountMessage.StateChange(Command.approve evt "")

            let origin = string evt.EntityId

            let cmd =
               DepositCashCommand(
                  Guid recipient.Identification,
                  evt.Data.DebitedAmount,
                  $"Account ({origin.Substring(origin.Length - 4)})",
                  // CorrelationId from sender transfer request traced to receiver's deposit.
                  evt.CorrelationId
               )

            let! coordinatorActorOpt = getActorRef mailbox "../../"

            coordinatorActorOpt.Value
            <! AccountCoordinatorMessage.StateChange cmd
   }

let start (mailbox: Actor<AccountMessage>) (persistence: AccountPersistence) =
   let handler mailbox evt =
      (issueTransferToRecipient mailbox persistence evt).Wait()
      Ignore

   spawn mailbox ActorName (props (actorOf2 handler))

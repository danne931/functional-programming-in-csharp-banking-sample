[<RequireQualifiedAccess>]
module TransferRecipientActor

open System
open Akkling

open Lib.Types
open BankTypes
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.Transfer.Api

let ActorName = "transfer_recipient"

let issueTransferToRecipient
   (mailbox: Actor<BankEvent<DebitedTransfer>>)
   (evt: BankEvent<DebitedTransfer>)
   =
   task {
      let recipient = evt.Data.Recipient

      if
         recipient.AccountEnvironment = RecipientAccountEnvironment.Internal
      then
         let origin = evt.EntityId.ToString()

         let cmd =
            DepositCashCommand(
               Guid recipient.Identification,
               evt.Data.DebitedAmount,
               $"Account ({origin.Substring(origin.Length - 4)})"
            )

         let! aref = getActorRef mailbox "../../"
         aref.Value <! AccountCoordinatorMessage.StateChange cmd
      else
         do! thirdPartyBankTransfer evt
   }

let start (mailbox: Actor<AccountMessage>) =
   let handler (mailbox: Actor<BankEvent<DebitedTransfer>>) evt =
      (issueTransferToRecipient mailbox evt).Wait()
      ignored ()

   spawn mailbox ActorName (props (actorOf2 handler))

using Echo;
using static Echo.Process;
using LanguageExt;
using static LanguageExt.Prelude;

using Lib.BankTypes;
using Bank.Account.Domain;
using Bank.Transfer.Domain;

namespace Bank.Transfer.Actors;

public static class TransferRecipientActor {
   public static async Task<Unit> IssueTransferToRecipient(
       AccountPersistence persistence,
       TransferPending evt
   ) {
      var recipient = evt.Recipient;
      var recipientId = new Guid(recipient.Identification);

      var accountExists = await persistence.exists(recipientId);
      if (!accountExists) {
         tellParent(
            TransferResponseToCommand.Reject(
               evt,
               TransferErr.RecipientNotFound(recipientId).Message)
         );
         return unit;
      }

      var origin = evt.EntityId.ToString();
      tell($"@accounts_{recipient.Identification}", new DepositCashCmd(
         recipientId,
         evt.Date,
         evt.DebitedAmount,
         Origin: $"Account ({origin.Substring(origin.Length - 4)})"
      ));

      tellParent(TransferResponseToCommand.Approve(evt));

      return unit;
   }

   public const string ActorName = "transfer_recipient";

   public static ProcessId Start(AccountPersistence persistence) =>
      spawn<TransferPending>(
         ActorName,
         evt => {
            IssueTransferToRecipient(persistence, evt).Wait();
         }
      );
}

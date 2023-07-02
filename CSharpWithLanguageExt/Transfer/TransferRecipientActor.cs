using Echo;
using static Echo.Process;
using LanguageExt;
using static LanguageExt.Prelude;

using Bank.Account.Domain;
using Bank.Transfer.API;
using Bank.Transfer.Domain;

namespace Bank.Transfer.Actors;

public static class TransferRecipientActor {
   public static async Task<Unit> IssueTransferToRecipient(DebitedTransfer evt) {
      var recipient = evt.Recipient;
      if (recipient.AccountEnvironment is RecipientAccountEnvironment.Internal) {
         var origin = evt.EntityId.ToString();
         tell($"@accounts_{recipient.Identification}", new DepositCashCmd(
            new Guid(recipient.Identification),
            evt.Date,
            evt.DebitedAmount,
            Origin: $"Account ({origin.Substring(origin.Length - 4)})"
         ));
      } else
         await BankTransferAPI.ThirdPartyBankTransfer(evt);

      return unit;
   }

   public const string ActorName = "transfer_recipient";

   public static ProcessId Start() =>
      spawn<DebitedTransfer>(
         ActorName,
         evt => {
            IssueTransferToRecipient(evt).Wait();
         }
      );
}
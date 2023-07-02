using EventStore.Client;
using LanguageExt;
using static LanguageExt.Prelude;

using Lib.Types;
using Bank.Account.API;
using Bank.Transfer.Domain;

namespace Bank.Transfer.API;

public static class BankTransferAPI {
   public static Task<bool> RecipientExists(
      EventStoreClient es,
      TransferRecipient recipient
   ) =>
      recipient.AccountEnvironment switch {
         RecipientAccountEnvironment.Internal => RecipientExistsInternally(es, recipient),
         RecipientAccountEnvironment.Domestic => RecipientExistsDomestically(recipient),
         RecipientAccountEnvironment.International => RecipientExistsInternationally(recipient)
      };

   public static Task<Unit> ThirdPartyBankTransfer(DebitedTransfer evt) =>
      evt.Recipient.AccountEnvironment switch {
         RecipientAccountEnvironment.Domestic => DomesticTransfer(evt),
         RecipientAccountEnvironment.International => InternationalTransfer(evt),
         _ =>
            throw new Err("Third party transfer requires a domestic or international account.")
      };

   private static Task<bool> RecipientExistsInternally(
      EventStoreClient es,
      TransferRecipient recipient
   ) =>
      AccountAPI.Exists(es, new Guid(recipient.Identification));

   private static Task<Unit> DomesticTransfer(DebitedTransfer evt) {
      // Simulate network request to send money to domestic bank
      /* 
      await DomesticTransferService.Handle(new {
         AccountNumber = cmd.Recipient.Identification,
         RoutingNumber = cmd.Recipient.RoutingNumber,
         Amount = cmd.Amount,
         Timestamp = cmd.Timestamp
      });
      */
      return Task.Delay(1*sec).ContinueWith(_ => unit);
   }

   // Simulate network request to send money to international bank
   private static Task<Unit> InternationalTransfer(
      DebitedTransfer evt
   ) =>
      Task.Delay(1*sec).ContinueWith(_ => unit);


   // Simulate network request to verify account in domestic bank
   private static Task<bool> RecipientExistsDomestically(
      TransferRecipient recipient
   ) =>
      Task.Delay(1*sec).ContinueWith(_ => true);

   // Simulate network request to verify account in international bank
   private static Task<bool> RecipientExistsInternationally(
      TransferRecipient recipient
   ) =>
      Task.Delay(1*sec).ContinueWith(_ => true);
}
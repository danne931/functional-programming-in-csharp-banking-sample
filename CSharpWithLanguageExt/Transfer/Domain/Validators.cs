using static LanguageExt.Prelude;

using Lib.Types;

namespace Bank.Transfer.Domain;

public static class Validators {
   public static Validator<TransferCmd> TransferValidation() =>
      cmd => {
         if (isEmpty(cmd.Recipient.Identification))
            return Fail<Err, TransferCmd>(TransferErr.InvalidDetails);
         if (cmd.Amount <= 0)
            return Fail<Err, TransferCmd>(TransferErr.InvalidAmount);

         return Success<Err, TransferCmd>(cmd);
      };

   public static Validator<RegisterTransferRecipientCmd> RegisterTransferRecipient() =>
      cmd => {
         var rec = cmd.Recipient;

         if (isEmpty(rec.LastName) || isEmpty(rec.Identification))
            return Fail<Err, RegisterTransferRecipientCmd>(
               TransferErr.InvalidRecipient
            );
         if (rec.Identification == cmd.EntityId.ToString())
            return Fail<Err, RegisterTransferRecipientCmd>(
               TransferErr.RecipientCanNotBeSelf
            );

         return Success<Err, RegisterTransferRecipientCmd>(cmd);
      };
}

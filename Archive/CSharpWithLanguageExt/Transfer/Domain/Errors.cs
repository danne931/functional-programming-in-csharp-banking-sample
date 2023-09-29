using Lib.Types;

namespace Bank.Transfer.Domain;

public static class TransferErr {
   public static Err InvalidDetails
      => new Err("RecipientIdentification required.");

   public static Err InvalidRecipient
      => new Err("LastName & Identification required.");

   public static Err RecipientCanNotBeSelf
      => new Err("Adding self as recipient is not allowed.");

   public static Err InvalidAccountEnvironment
      => new Err("Only internal transfers allowed.");

   public static Err InvalidAmount
      => new Err("Transfer amount must be greater than 0.");

   public static Err RecipientNotFound(Guid id)
      => new Err($"Recipient not found: {id.ToString()}");

   public static Err RecipientRegistrationRequired(TransferCmd cmd)
      => new Err("Recipient registration required before issuing transfers. " +
                 $"{cmd.Recipient.LastName}/{cmd.Recipient.Identification}");

   public static Err RecipientAlreadyRegistered(RegisterTransferRecipientCmd cmd)
      => new Err($"Transfer recipient {cmd.Recipient.LastName}" +
                 $"/{cmd.Recipient.Identification} " +
                 $"already added to account: {cmd.EntityId}");
}

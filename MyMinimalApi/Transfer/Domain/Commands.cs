using OneOf;
using Lib.Types;

namespace Bank.Transfer.Domain;

using TransferRecipientEvent = OneOf<
   RegisteredInternalTransferRecipient,
   RegisteredDomesticTransferRecipient,
   RegisteredInternationalTransferRecipient
>;

public record TransferCmd(
   Guid EntityId,
   string RecipientLastName,
   string RecipientIdentification,
   DateTime Date,
   decimal Amount,
   string Reference
)
: Command(EntityId)
{
   public DebitedTransfer ToEvent() => new(
      EntityId: EntityId,
      Date: Date,
      RecipientLastName: RecipientLastName,
      RecipientIdentification: RecipientIdentification,
      DebitedAmount: Amount,
      Reference: Reference,
      Timestamp: Timestamp
   );
}

public record RegisterTransferRecipientCmd(
   Guid EntityId,
   TransferRecipient Recipient
)
: Command(EntityId)
{
   public TransferRecipientEvent ToEvent() =>
      Recipient.AccountEnvironment switch {
         RecipientAccountEnvironment.Internal => new RegisteredInternalTransferRecipient(
            EntityId: EntityId,
            Timestamp: Timestamp,
            LastName: Recipient.LastName,
            FirstName: Recipient.FirstName,
            AccountNumber: Recipient.Identification
         ),
         RecipientAccountEnvironment.Domestic => new RegisteredDomesticTransferRecipient(
            EntityId: EntityId,
            Timestamp: Timestamp,
            LastName: Recipient.LastName,
            FirstName: Recipient.FirstName,
            AccountNumber: Recipient.Identification,
            RoutingNumber: Recipient.RoutingNumber
         ),
         RecipientAccountEnvironment.International => new RegisteredInternationalTransferRecipient(
            EntityId: EntityId,
            LastName: Recipient.LastName,
            FirstName: Recipient.FirstName,
            Identification: Recipient.Identification,
            IdentificationStrategy: Recipient.IdentificationStrategy,
            Currency: Recipient.Currency,
            Timestamp: Timestamp
         )
      };
}
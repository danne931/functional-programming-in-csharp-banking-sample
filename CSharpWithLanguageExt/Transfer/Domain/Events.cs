using OneOf;

using Lib.Types;

namespace Bank.Transfer.Domain;

using TransferRecipientEvent = OneOf<
   RegisteredInternalTransferRecipient,
   RegisteredDomesticTransferRecipient,
   RegisteredInternationalTransferRecipient
>;

public record TransferPending(
   Guid EntityId,
   DateTime Date,
   DateTime Timestamp,
   TransferRecipient Recipient,
   decimal DebitedAmount,
   string Reference
)
: Event(EntityId, Timestamp, nameof(TransferPending), 1.0F);

public record TransferApproved(
   Guid EntityId,
   DateTime Date,
   DateTime Timestamp,
   TransferRecipient Recipient,
   decimal DebitedAmount
)
: Event(EntityId, Timestamp, nameof(TransferApproved), 1.0F);

public record TransferRejected(
   Guid EntityId,
   DateTime Date,
   DateTime Timestamp,
   TransferRecipient Recipient,
   decimal DebitedAmount,
   string Reason
)
: Event(EntityId, Timestamp, nameof(TransferRejected), 1.0F);

public record RegisteredInternalTransferRecipient(
   Guid EntityId,
   DateTime Timestamp,
   string LastName,
   string FirstName,
   string AccountNumber
)
: Event(EntityId, Timestamp, nameof(RegisteredInternalTransferRecipient))
{
   public TransferRecipient AsTransferRecipient() =>
      new TransferRecipient(
         LastName,
         FirstName,
         AccountNumber,
         RecipientAccountEnvironment.Internal,
         RecipientAccountIdentificationStrategy.AccountID
      );
}

public record RegisteredDomesticTransferRecipient(
   Guid EntityId,
   string LastName,
   string FirstName,
   string RoutingNumber,
   string AccountNumber,
   DateTime Timestamp
)
: Event(EntityId, Timestamp, nameof(RegisteredDomesticTransferRecipient))
{
   public TransferRecipient AsTransferRecipient() =>
      new TransferRecipient(
         LastName,
         FirstName,
         AccountNumber,
         RecipientAccountEnvironment.Domestic,
         RecipientAccountIdentificationStrategy.AccountID,
         RoutingNumber
      );
}

public record RegisteredInternationalTransferRecipient(
   Guid EntityId,
   string LastName,
   string FirstName,
   string Identification,
   RecipientAccountIdentificationStrategy IdentificationStrategy,
   string Currency,
   DateTime Timestamp
)
: Event(EntityId, Timestamp, nameof(RegisteredInternationalTransferRecipient))
{
   public TransferRecipient AsTransferRecipient() =>
      new TransferRecipient(
         LastName,
         FirstName,
         Identification,
         RecipientAccountEnvironment.International,
         IdentificationStrategy,
         Currency: Currency
      );
}

public static class TransferEventsExt {
   public static Event Unwrap(this TransferRecipientEvent evtWrapped) =>
      evtWrapped.Match<Event>(
         _ => evtWrapped.AsT0,
         _ => evtWrapped.AsT1,
         _ => evtWrapped.AsT2
      );
}

public static class TransferResponseToCommand {
   public static ApproveTransferCmd Approve(TransferPending evt) {
      return new ApproveTransferCmd(
         evt.EntityId,
         evt.Recipient,
         evt.Date,
         evt.DebitedAmount
      );
   }

   public static RejectTransferCmd Reject(TransferPending evt, string reason) {
      return new RejectTransferCmd(
         evt.EntityId,
         evt.Recipient,
         evt.Date,
         evt.DebitedAmount,
         reason
      );
   }
}

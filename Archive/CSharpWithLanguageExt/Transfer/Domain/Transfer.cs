namespace Bank.Transfer.Domain;

public record TransferRecipient(
   string LastName,
   string FirstName,
   string Identification,
   RecipientAccountEnvironment AccountEnvironment,
   RecipientAccountIdentificationStrategy IdentificationStrategy,
   string? RoutingNumber = null,
   string Currency = "USD"
);

public enum RecipientAccountIdentificationStrategy {
   AccountID,
   SwiftBIC,
   IBAN,
   NationalID
}

public enum RecipientAccountEnvironment {
   Internal,
   Domestic,
   International
}
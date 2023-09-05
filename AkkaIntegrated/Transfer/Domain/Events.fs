namespace Bank.Transfer.Domain

open System

open Lib.Types

type AckReceipt = string

type RecipientAccountEnvironment =
   | Internal
   | Domestic
   | International

type RecipientAccountIdentificationStrategy =
   | AccountId
   | SwiftBIC
   | IBAN
   | NationalID

type TransferRecipient = {
   LastName: string
   FirstName: string
   Identification: string
   AccountEnvironment: RecipientAccountEnvironment
   IdentificationStrategy: RecipientAccountIdentificationStrategy
   RoutingNumber: string option
   Currency: Currency
}

type TransferPending = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   Reference: string option
}

type TransferApproved = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   AckReceipt: AckReceipt option
}

type TransferRejected = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   Reason: string
}

type RegisteredInternalTransferRecipient = {
   LastName: string
   FirstName: string
   AccountNumber: string
   Currency: Currency
   AccountEnvironment: RecipientAccountEnvironment
} with

   member x.toRecipient() = {
      FirstName = x.FirstName
      LastName = x.LastName
      AccountEnvironment = x.AccountEnvironment
      Identification = x.AccountNumber
      IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
      Currency = x.Currency
      RoutingNumber = None
   }

type RegisteredDomesticTransferRecipient = {
   LastName: string
   FirstName: string
   RoutingNumber: string option
   AccountNumber: string
   Currency: Currency
   AccountEnvironment: RecipientAccountEnvironment
} with

   member x.toRecipient() = {
      FirstName = x.FirstName
      LastName = x.LastName
      AccountEnvironment = x.AccountEnvironment
      Identification = x.AccountNumber
      IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
      Currency = x.Currency
      RoutingNumber = x.RoutingNumber
   }

type RegisteredInternationalTransferRecipient = {
   LastName: string
   FirstName: string
   Identification: string
   IdentificationStrategy: RecipientAccountIdentificationStrategy
   Currency: Currency
   AccountEnvironment: RecipientAccountEnvironment
} with

   member x.toRecipient() = {
      FirstName = x.FirstName
      LastName = x.LastName
      AccountEnvironment = x.AccountEnvironment
      Identification = x.Identification
      IdentificationStrategy = x.IdentificationStrategy
      Currency = x.Currency
      RoutingNumber = None
   }

type TransferDeposited = {
   DepositedAmount: decimal
   Origin: string
}

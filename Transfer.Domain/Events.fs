namespace Bank.Transfer.Domain

open System

open Lib.Types

type TransferPending = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   Reference: string option
   Status: TransferProgress
}

type TransferProgressUpdate = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
   Status: TransferProgress
}

type TransferApproved = {
   Recipient: TransferRecipient
   Date: DateTime
   DebitedAmount: decimal
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

type TransferDeposited = {
   DepositedAmount: decimal
   Origin: string
}

module TransferEventToTransaction =
   let fromPending (evt: BankEvent<TransferPending>) = {
      SenderAccountId = evt.EntityId
      TransactionId = evt.CorrelationId
      Recipient = evt.Data.Recipient
      Amount = evt.Data.DebitedAmount
      Date = evt.Data.Date
      Status = evt.Data.Status
   }

   let fromProgressUpdate (evt: BankEvent<TransferProgressUpdate>) = {
      SenderAccountId = evt.EntityId
      TransactionId = evt.CorrelationId
      Recipient = evt.Data.Recipient
      Amount = evt.Data.DebitedAmount
      Date = evt.Data.Date
      Status = evt.Data.Status
   }

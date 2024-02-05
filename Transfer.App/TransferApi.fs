module Bank.Transfer.Api

open FsToolkit.ErrorHandling

open Lib.Postgres
open Bank.Transfer.Domain
open AccountSqlMapper

// NOTE: Date Filter:
// Include in progress check if an hour has elapsed since transfer initiated.
//
// NOTE: Transfer Status Filter:
// Include in progress check if the initial transfer request has
// been acknowledged as received by the mock 3rd party bank.
// This avoids transfer rejections due to a ProgressCheck being
// received by the mock 3rd party bank before a TransferRequest
// in cases where a TransferRequest was rescheduled when the
// domestic transfer actor's circuit breaker was open.
let getProgressCheckReadyDomesticTransfers (lookbackMinutes: int) () = asyncResultOption {
   let reader (read: RowReader) =
      read.text "txns" |> Serialization.deserializeUnsafe<TransferTransaction>

   let recipientEnvironmentPath = "{recipient,accountEnvironment}"
   let statusPath = "{status,Case}"

   let! transfers =
      pgQuery<TransferTransaction>
         $"""
         SELECT txns
         FROM
            accounts,
            jsonb_array_elements(accounts.{AccountFields.inProgressTransfers}) as txns
         WHERE
            accounts.{AccountFields.inProgressTransfersCount} > 0
            AND txns #>> '{recipientEnvironmentPath}' = 'Domestic'
            AND txns #>> '{statusPath}' = 'InProgress'
            AND (txns ->> 'date')::timestamptz < current_timestamp - '{lookbackMinutes} minutes'::interval
         """
         None
         reader

   return transfers
}

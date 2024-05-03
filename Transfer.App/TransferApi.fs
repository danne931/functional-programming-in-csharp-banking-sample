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

   let recipientEnvironmentPath = "{Recipient,AccountEnvironment}"
   let statusPath = "{Status,0}"

   let inProgressTransfersPath =
      AccountSqlMapper.table + "." + AccountFields.inProgressTransfers

   let inProgressTransfersCountPath =
      AccountSqlMapper.table + "." + AccountFields.inProgressTransfersCount

   let! transfers =
      pgQuery<TransferTransaction>
         $"""
         SELECT txns
         FROM
            {AccountSqlMapper.table},
            jsonb_array_elements({inProgressTransfersPath}) as txns
         WHERE
            {inProgressTransfersCountPath} > 0
            AND txns #>> '{recipientEnvironmentPath}' = 'Domestic'
            AND txns #>> '{statusPath}' = 'InProgress'
            AND (txns ->> 'Date')::timestamptz < current_timestamp - '{lookbackMinutes} minutes'::interval
         """
         None
         reader

   return transfers
}

module Bank.Transfer.Api

open FsToolkit.ErrorHandling

open Lib.Postgres
open Bank.Transfer.Domain

let getInProgressTransfers () = asyncResultOption {
   let! transfers =
      pgQuery<TransferTransaction list>
         "SELECT in_progress_transfers FROM accounts WHERE in_progress_transfers_count > 0"
         None
      <| fun (read: RowReader) ->
         read.text "in_progress_transfers"
         |> Serialization.deserializeUnsafe<Map<string, TransferTransaction>>
         |> Map.values
         |> List.ofSeq

   return transfers |> List.collect id
}

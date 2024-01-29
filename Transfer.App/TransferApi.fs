module Bank.Transfer.Api

open FsToolkit.ErrorHandling

open Lib.Postgres
open Bank.Account.Domain
open Bank.Transfer.Domain

let getInProgressTransfers () = asyncResultOption {
   let transfers = AccountFields.inProgressTransfers
   let count = AccountFields.inProgressTransfersCount

   let! transfers =
      pgQuery<TransferTransaction list>
         $"SELECT {transfers} FROM accounts WHERE {count} > 0"
         None
         (AccountSqlReader.inProgressTransfers >> Map.values >> List.ofSeq)

   return transfers |> List.collect id
}

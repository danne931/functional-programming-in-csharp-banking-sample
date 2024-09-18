namespace Bank.Transfer.Domain

open System

type TransferId =
   | TransferId of Guid

   override x.ToString() =
      let (TransferId id) = x
      string id

module TransferId =
   let get (transferId: TransferId) =
      let (TransferId id) = transferId
      id

[<RequireQualifiedAccess>]
type TransferCategory =
   | InternalWithinOrg
   | InternalBetweenOrgs
   | Domestic

module TransferCategory =
   let fromString (str: string) : TransferCategory option =
      match str.ToLower() with
      | "internalwithingorg" -> Some TransferCategory.InternalWithinOrg
      | "internalbetweenorgs" -> Some TransferCategory.InternalBetweenOrgs
      | "domestic" -> Some TransferCategory.Domestic
      | _ -> None

   let fromStringUnsafe str : TransferCategory =
      match fromString str with
      | Some s -> s
      | None -> failwith "Error attempting to cast string to TransferCategory"

[<RequireQualifiedAccess>]
type RecipientAccountEnvironment =
   | InternalWithinOrg
   | InternalBetweenOrgs
   | Domestic

module RecipientAccountEnvironment =
   let fromString (str: string) : RecipientAccountEnvironment option =
      match str.ToLower() with
      | "internalwithinorg" ->
         Some RecipientAccountEnvironment.InternalWithinOrg
      | "internalbetweenorgs" ->
         Some RecipientAccountEnvironment.InternalBetweenOrgs
      | "domestic" -> Some RecipientAccountEnvironment.Domestic
      | _ -> None

   let fromStringUnsafe str : RecipientAccountEnvironment =
      match fromString str with
      | Some s -> s
      | None ->
         failwith
            "Error attempting to cast string to RecipientAccountEnvironment"

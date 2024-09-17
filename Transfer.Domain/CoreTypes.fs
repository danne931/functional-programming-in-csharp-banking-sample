namespace Bank.Transfer.Domain

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

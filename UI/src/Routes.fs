[<RequireQualifiedAccess>]
module Routes

open Feliz.Router

open Bank.Account.UIDomain
open Lib.SharedTypes

[<RequireQualifiedAccess>]
type AccountUrl =
   | Account
   | AccountSelected of AccountId
   | AccountSelectedWithQuery of AccountId * AccountBrowserQuery
   | NotFound

module AccountUrl =
   let parse =
      function
      // Matches /
      | [] -> AccountUrl.Account
      // Matches /{accountId:Guid}
      | [ Route.Guid accountId ] ->
         AccountUrl.AccountSelected(AccountId accountId)
      // /{accountId:Guid}?action=deposit&isCategorized=false&date=Last30Days
      | [ Route.Guid accountId; Route.Query queryParams ] ->
         let query = AccountBrowserQuery.fromQueryParams queryParams
         AccountUrl.AccountSelectedWithQuery(AccountId accountId, query)
      | _ -> AccountUrl.NotFound

   let accountIdMaybe =
      function
      | AccountUrl.AccountSelected id -> Some id
      | AccountUrl.AccountSelectedWithQuery(id, _) -> Some id
      | _ -> None

   let transactionIdMaybe =
      function
      | AccountUrl.AccountSelectedWithQuery(_, query) -> query.Transaction
      | _ -> None

[<RequireQualifiedAccess>]
type IndexUrl =
   | Account of AccountUrl
   | Reporting
   | NotFound

module IndexUrl =
   let parse (segments: string list) =
      // Temporarily redirect Index page to Accounts.
      let segments = if segments.IsEmpty then [ "account" ] else segments

      match segments with
      //| "reporting" :: reportingSegments ->
      // Matches /account/{AccountDashboard.Url}
      | "account" :: accountSegments ->
         IndexUrl.Account(AccountUrl.parse accountSegments)
      | _ -> IndexUrl.NotFound

   let accountBrowserQuery () =
      let defaultQuery = {
         Category = None
         MoneyFlow = None
         Amount = None
         Date = None
         Action = None
         Transaction = None
      }

      match parse (Router.currentUrl ()) with
      | IndexUrl.Account url ->
         match url with
         | AccountUrl.AccountSelectedWithQuery(_, query) -> query
         | _ -> defaultQuery
      | _ -> defaultQuery

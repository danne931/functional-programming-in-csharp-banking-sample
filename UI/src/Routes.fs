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
   [<Literal>]
   let BasePath = "account"

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
type EmployeeUrl =
   | Employees
   | NotFound

module EmployeeUrl =
   [<Literal>]
   let BasePath = "employees"

   let parse =
      function
      | [] -> EmployeeUrl.Employees
      | _ -> EmployeeUrl.NotFound

[<RequireQualifiedAccess>]
type CardUrl =
   | Cards
   | NotFound

module CardUrl =
   [<Literal>]
   let BasePath = "cards"

   let parse =
      function
      | [] -> CardUrl.Cards
      | _ -> CardUrl.NotFound

[<RequireQualifiedAccess>]
type IndexUrl =
   | Account of AccountUrl
   | Employees of EmployeeUrl
   | Cards of CardUrl
   | Reporting
   | NotFound

module IndexUrl =
   let parse (segments: string list) =
      // Temporarily redirect Index page to Accounts.
      let segments =
         if segments.IsEmpty then
            [ AccountUrl.BasePath ]
         else
            segments

      match segments with
      //| "reporting" :: reportingSegments ->
      // Matches /account/{AccountUrl}
      | AccountUrl.BasePath :: segments ->
         IndexUrl.Account(AccountUrl.parse segments)
      // Matches /employees/{EmployeeUrl}
      | EmployeeUrl.BasePath :: segments ->
         IndexUrl.Employees(EmployeeUrl.parse segments)
      // Matches /cards/{CardUrl}
      | CardUrl.BasePath :: segments -> IndexUrl.Cards(CardUrl.parse segments)
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

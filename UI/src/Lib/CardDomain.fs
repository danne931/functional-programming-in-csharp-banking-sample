module UIDomain.Card

open Bank.Employee.Domain
open UIDomain.Employee
open Lib.SharedTypes
open Lib.NetworkQuery

type CardsMaybe = Result<CardWithMetrics list option, Err>

type SelectedAccount = { Id: AccountId; Name: string }

module SelectedAccount =
   let parse =
      Serialization.deserialize<SelectedAccount list> >> Result.toOption

   let listToDisplay =
      List.fold
         (fun acc (filter: SelectedAccount) ->
            if acc = "" then filter.Name else $"{acc}, {filter.Name}")
         ""

[<RequireQualifiedAccess>]
type CardActionView =
   | PurchaseLimit
   | CardAccess
   | Create
   | CardDetail of CardId

type CardBrowserQuery = {
   CreatedAt: DateFilter option
   Action: CardActionView option
   SelectedEmployees: (SelectedEmployee list) option
   SelectedAccounts: (SelectedAccount list) option
   Amount: AmountFilter option
} with

   member x.ChangeDetection =
      Serialization.serialize {|
         SelectedAccounts = x.SelectedAccounts
         SelectedEmployees = x.SelectedEmployees
         Amount = x.Amount
         CreatedAt = x.CreatedAt
      |}

module CardBrowserQuery =
   let toQueryParams (query: CardBrowserQuery) : (string * string) list =
      let agg =
         query.Amount
         |> Option.map AmountFilter.toQuery
         |> Option.defaultValue []

      let agg =
         match query.Action with
         | Some view -> ("action", string view) :: agg
         | None -> agg

      let agg =
         match query.SelectedEmployees with
         | None -> agg
         | Some employees ->
            ("employees", Serialization.serialize employees) :: agg

      let agg =
         match query.SelectedAccounts with
         | None -> agg
         | Some selected ->
            ("accounts", Serialization.serialize selected) :: agg

      let agg =
         match query.CreatedAt with
         | None -> agg
         | Some(DateFilter.Custom(startDate, endDate)) ->
            ("createdAt", DateTime.rangeAsQueryString startDate endDate) :: agg
         | Some filter -> ("createdAt", string filter) :: agg

      agg

   let private (|CardDetail|_|) (input: string) =
      let needle = "CardDetail "

      if input.StartsWith(needle) then
         input.Substring(needle.Length)
         |> Guid.parseOptional
         |> Option.map CardId
      else
         None

   let fromQueryParams
      (queryParams: (string * string) list)
      : CardBrowserQuery
      =
      let queryParams = Map.ofList queryParams

      {
         Amount = AmountFilter.fromQueryString queryParams
         SelectedAccounts =
            Map.tryFind "accounts" queryParams
            |> Option.bind SelectedAccount.parse
         SelectedEmployees =
            Map.tryFind "employees" queryParams |> Option.bind parseEmployees
         Action =
            Map.tryFind "action" queryParams
            |> Option.bind (function
               | "Create" -> Some CardActionView.Create
               | "CardAccess" -> Some CardActionView.CardAccess
               | "PurchaseLimit" -> Some CardActionView.PurchaseLimit
               | CardDetail id -> Some(CardActionView.CardDetail id)
               | view ->
                  Log.error $"Card action view not implemented: {view}"
                  None)
         CreatedAt =
            Map.tryFind "createdAt" queryParams
            |> Option.bind DateFilter.fromString
      }

   let empty: CardBrowserQuery = {
      Action = None
      SelectedAccounts = None
      SelectedEmployees = None
      Amount = None
      CreatedAt = None
   }

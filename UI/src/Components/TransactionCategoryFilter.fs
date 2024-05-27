module TransactionCategoryFilter

open Feliz
open System

open Bank.Account.Domain
open Lib.TransactionQuery

let renderCategoryAssignmentStatusFilter
   (category: CategoryFilter option)
   (onChange: CategoryFilter option -> unit)
   =
   let options = [
      "all", "All Transactions", category.IsNone

      "cat",
      "Categorized",
      match category with
      | Some(CategoryFilter.IsCategorized isCat) -> isCat = true
      | Some(CategoryFilter.CategoryIds _) -> true
      | _ -> false

      "notCat",
      "Uncategorized",
      match category with
      | Some(CategoryFilter.IsCategorized isCat) -> isCat = false
      | _ -> false
   ]

   Html.fieldSet [
      for value, text, isChecked in options do
         Html.label [
            Html.input [
               attr.type' "radio"
               attr.name "categoryAssignmentStatus"
               attr.isChecked isChecked
               attr.ariaChecked isChecked
               attr.value value

               attr.onChange (fun (option: string) ->
                  let filter =
                     if option = "cat" then
                        Some <| CategoryFilter.IsCategorized true
                     else if option = "notCat" then
                        Some <| CategoryFilter.IsCategorized false
                     else
                        None

                  onChange filter)
            ]
            Html.text text
         ]
   ]

let renderCategoriesFilter
   (categoryOpt: CategoryFilter option)
   (categories: Map<int, TransactionCategory>)
   (onChange: CategoryFilter option -> unit)
   =
   React.fragment [
      for cat in categories.Values ->
         Html.label [
            Html.input [
               attr.type' "checkbox"
               attr.name (string cat.Id)
               attr.isChecked (
                  match categoryOpt with
                  | Some(CategoryFilter.CategoryIds catIds) ->
                     catIds |> List.exists (fun id -> id = cat.Id)
                  | _ -> false
               )

               attr.onChange (fun (_: Browser.Types.Event) ->
                  let filter =
                     match categoryOpt with
                     | Some(CategoryFilter.CategoryIds catIds) ->
                        let withoutClicked =
                           catIds |> List.filter (fun id -> id <> cat.Id)

                        if withoutClicked.Length = catIds.Length then
                           Some <| CategoryFilter.CategoryIds(cat.Id :: catIds)
                        else if not withoutClicked.IsEmpty then
                           Some <| CategoryFilter.CategoryIds withoutClicked
                        else
                           None
                     | _ -> Some <| CategoryFilter.CategoryIds [ cat.Id ]

                  onChange filter)
            ]

            Html.text cat.Name
         ]
   ]

[<ReactComponent>]
let TransactionCategoryFilterComponent
   (category: CategoryFilter option)
   (categories: Map<int, TransactionCategory>)
   (onChange: CategoryFilter option -> unit)
   =
   let searchInput, setSearchInput = React.useState ""
   let searchRef = React.useInputRef ()

   let categories =
      if String.IsNullOrWhiteSpace searchInput then
         categories
      else
         categories
         |> Map.filter (fun _ cat ->
            cat.Name.ToLower().Contains(searchInput.ToLower()))

   React.useEffectOnce (fun () ->
      match searchRef.current with
      | Some searchInput -> searchInput.focus ()
      | None -> ())

   React.fragment [
      Html.input [
         attr.type' "search"
         attr.name "CategorySearch"
         attr.placeholder "Search for a category"
         attr.ariaLabel "Search for a category"
         attr.value searchInput
         attr.onChange setSearchInput
         attr.ref searchRef
      ]

      renderCategoryAssignmentStatusFilter category onChange

      Html.small "Categories"

      renderCategoriesFilter category categories onChange
   ]

module TransactionCategoryFilter

open Feliz
open System

open Bank.Account.Domain
open Lib.NetworkQuery

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

      CheckboxFieldset.render {|
         Options =
            categories.Values
            |> Seq.toList
            |> List.map (fun cat -> { Id = cat.Id; Display = cat.Name })
         SelectedItems =
            category
            |> Option.bind (function
               | CategoryFilter.IsCategorized _ -> None
               | CategoryFilter.CategoryIds ids -> Some ids)
         OnChange =
            fun ids ->
               let ids = ids |> Option.map CategoryFilter.CategoryIds
               onChange ids
      |}
   ]

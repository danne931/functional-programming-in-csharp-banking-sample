module TransactionCategoryProvider

open Feliz

[<ReactComponent>]
let TransactionCategoryProvider (child: Fable.React.ReactElement) =
   let categories, setCategories = React.useState Map.empty

   React.useEffectOnce (fun () ->
      if categories.IsEmpty then
         async {
            match! AncillaryTransactionInfoService.getCategories () with
            | Ok categories ->
               [ for cat in categories -> cat.Id, cat ]
               |> Map.ofList
               |> setCategories
            | Error err ->
               Log.error $"Error fetching transaction categories: {err}"
         }
         |> Async.StartImmediate)

   React.contextProvider (
      Contexts.transactionCategoryContext,
      categories,
      child
   )

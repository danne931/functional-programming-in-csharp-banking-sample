module CardActionMenu

open Feliz
open Feliz.Router

open UIDomain.Employee

let navigate (view: CardActionView) =
   let queryString =
      {
         Routes.IndexUrl.cardBrowserQuery () with
            Action = Some view
      }
      |> CardBrowserQuery.toQueryParams
      |> Router.encodeQueryString

   Router.navigate [| Routes.CardUrl.BasePath; queryString |]

let private renderMenuButton (view: CardActionView) =
   Html.button [
      attr.classes [ "outline" ]

      attr.onClick (fun _ -> navigate view)

      attr.text (
         match view with
         | CardActionView.DailyDebitLimit -> "Daily Debit Limit"
         | CardActionView.CardAccess -> "Card Access"
      )
   ]

let render () =
   classyNode Html.div [ "action-menu" ] [
      classyNode Html.div [ "grid" ] [
         renderMenuButton CardActionView.CardAccess
      ]

      classyNode Html.div [ "grid" ] [
         renderMenuButton CardActionView.DailyDebitLimit
      ]
   ]

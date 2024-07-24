module CardActionMenu

open Feliz
open Feliz.Router
open Fable.FontAwesome

open UIDomain.Card

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
      attr.classes [ "outline"; "grid" ]

      attr.onClick (fun _ -> navigate view)

      attr.children [
         match view with
         | CardActionView.Create ->
            Html.span [ Fa.i [ Fa.Solid.CreditCard ] [] ]
            Html.span "Add Card"
         | CardActionView.PurchaseLimit ->
            Html.span [ Fa.i [ Fa.Solid.SlidersH ] [] ]
            Html.span "Purchase Limits"
         | CardActionView.CardAccess ->
            Html.span [ Fa.i [ Fa.Solid.Key ] [] ]
            Html.span "Card Access"
      ]
   ]

let render () =
   classyNode Html.div [ "action-menu" ] [
      classyNode Html.div [ "grid" ] [ renderMenuButton CardActionView.Create ]

      classyNode Html.div [ "grid" ] [
         renderMenuButton CardActionView.CardAccess
      ]

      classyNode Html.div [ "grid" ] [
         renderMenuButton CardActionView.PurchaseLimit
      ]
   ]

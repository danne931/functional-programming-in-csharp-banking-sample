module CardDashboard

open Feliz
open Feliz.UseElmish
open Elmish
open Feliz.Router

open AsyncUtil
open Lib.SharedTypes

[<ReactComponent>]
let CardDashboardComponent (url: Routes.CardUrl) =
   //let state, dispatch = React.useElmish (init, update, [||])

   classyNode Html.div [ "card-dashboard" ] [
      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [ Html.h5 "Cards" ]

            Html.aside []
         ]
      ]
   ]

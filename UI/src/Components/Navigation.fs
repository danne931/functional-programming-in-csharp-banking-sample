module Navigation

open Feliz
open System

open Bank.Account.Domain
open AccountSelection

[<ReactComponent>]
let NavigationComponent
   (accounts: Map<Guid, Account> option)
   (selectedAccountId: Guid option)
   =
   classyNode Html.nav [ "container-fluid" ] [
      Html.ul [
         Html.li [
            Html.a [
               attr.href ""
               attr.onClick _.preventDefault()
               attr.children [ Html.strong "Bank" ]
            ]
         ]
      ]

      match accounts with
      | None -> Html.none
      | Some accounts -> AccountSelectionComponent selectedAccountId accounts
   ]

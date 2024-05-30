module Navigation

open Feliz

open Bank.Account.Domain
open AccountSelection
open Lib.SharedTypes

[<ReactComponent>]
let NavigationComponent
   (accounts: Map<AccountId, AccountProfile> option)
   (selectedAccountId: AccountId option)
   =
   classyNode Html.nav [ "container-fluid" ] [
      Html.ul [
         Html.li [
            Html.a [
               attr.href ""
               attr.onClick (fun e -> e.preventDefault ())
               attr.children [ Html.strong "Bank" ]
            ]
         ]
      ]

      match accounts with
      | None -> Html.none
      | Some accounts -> AccountSelectionComponent selectedAccountId accounts
   ]

module AccountSelection

open Feliz
open Feliz.Router
open System

open Bank.Account.Domain

let renderAccountListItem (selectedAccountId: Guid option) (account: Account) =
   Html.li [
      Html.a [
         attr.text $"{account.Name} - {account.Email}"
         attr.href ""
         attr.value account.EntityId
         attr.onClick (fun e ->
            e.preventDefault ()
            Router.navigate ("account", string account.EntityId))

         match selectedAccountId with
         | Some selectedId when selectedId = account.EntityId ->
            attr.classes [ "selected" ]
         | _ -> ()
      ]
   ]

[<ReactComponent>]
let AccountSelectionComponent
   (selectedAccountId: Guid option)
   (accounts: Map<Guid, Account>)
   =
   let isAccountSelectionOpen, setAccountSelectionOpen = React.useState false

   Html.ul [
      attr.custom (
         "data-tooltip",
         "Select an account to process transactions on."
      )
      attr.custom ("data-placement", "left")
      attr.children [
         Html.li [
            Html.details [
               attr.role "list"
               attr.custom ("dir", "rtl")
               attr.isOpen isAccountSelectionOpen
               attr.onClick (fun e ->
                  e.preventDefault ()
                  setAccountSelectionOpen (not isAccountSelectionOpen))

               attr.children [
                  Html.summary [
                     attr.custom ("aria-haspopup", "listbox")
                     attr.role "link"
                     attr.classes [ "contrast" ]
                     attr.text "Accounts"
                  ]

                  Html.ul [
                     attr.id "accounts-list"
                     attr.role "listbox"
                     attr.children [
                        for account in accounts.Values ->
                           renderAccountListItem selectedAccountId account
                     ]
                  ]
               ]
            ]
         ]
      ]
   ]

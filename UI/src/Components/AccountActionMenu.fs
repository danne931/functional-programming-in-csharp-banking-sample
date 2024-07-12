module AccountActionMenu

open Feliz
open Feliz.Router

open Bank.Account.Domain
open UIDomain.Account

let private renderMenuButton (account: Account) (view: AccountActionView) =
   Html.button [
      attr.classes [ "outline" ]

      attr.onClick (fun _ ->
         let view =
            if
               AccountActions.shouldRedirectToRegisterRecipient account view
            then
               AccountActionView.RegisterTransferRecipient
            else
               view

         AccountActions.navigation account.AccountId (Some view)
         |> Router.navigate)

      attr.text (
         match view with
         | AccountActionView.Debit -> "Debit Card Purchase"
         | AccountActionView.RegisterTransferRecipient ->
            "Add a Transfer Recipient"
         | _ -> string view
      )
   ]

let render (account: Account) =
   classyNode Html.div [ "action-menu" ] [
      classyNode Html.div [ "grid" ] [
         renderMenuButton account AccountActionView.Debit
      ]

      classyNode Html.div [ "grid" ] [
         renderMenuButton account AccountActionView.Deposit
      ]

      classyNode Html.div [ "grid" ] [
         renderMenuButton account AccountActionView.Transfer
      ]

      classyNode Html.div [ "grid" ] [
         renderMenuButton account AccountActionView.RegisterTransferRecipient
      ]
   ]

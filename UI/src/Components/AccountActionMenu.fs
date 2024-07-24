module AccountActionMenu

open Feliz
open Feliz.Router
open Fable.FontAwesome

open Bank.Account.Domain
open UIDomain.Account

let private renderMenuButton (account: Account) (view: AccountActionView) =
   Html.button [
      attr.classes [ "outline"; "grid" ]

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

      attr.children [
         match view with
         | AccountActionView.Deposit ->
            Html.span [ Fa.i [ Fa.Solid.PiggyBank ] [] ]
            Html.span "Deposit"
         | AccountActionView.Debit ->
            Html.span [ Fa.i [ Fa.Solid.CreditCard ] [] ]
            Html.span "Debit Card Purchase"
         | AccountActionView.Transfer ->
            Html.span [ Fa.i [ Fa.Solid.ArrowsAltH ] [] ]
            Html.span "Transfer"
         | AccountActionView.RegisterTransferRecipient ->
            Html.span [ Fa.i [ Fa.Solid.UserPlus ] [] ]
            Html.span "Add a Transfer Recipient"
      ]
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

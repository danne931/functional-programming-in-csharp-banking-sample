module AccountDashboard

open Feliz
open Feliz.Router
open Fable.FontAwesome

open Bank.Account.Domain
open Bank.Employee.Domain
open UIDomain.Account
open Bank.Account.Forms.AccountCreateForm

[<ReactComponent>]
let AccountNumberComponent (profile: AccountProfile) =
   let display, toggleDisplay = React.useState false

   React.fragment [
      if display then
         Html.b (string profile.AccountNumber)
      else
         Html.b $"*****{profile.AccountNumber.Last4}"

      Html.a [
         attr.href ""

         attr.onClick (fun e ->
            e.preventDefault ()
            toggleDisplay (not display))

         attr.children [
            if display then
               Fa.i [ Fa.Solid.EyeSlash ] []
            else
               Fa.i [ Fa.Solid.Eye ] []
         ]
      ]
   ]

let private onClose _ =
   Router.navigate Routes.AccountUrl.BasePath

[<ReactComponent>]
let AccountDashboardComponent (url: Routes.AccountUrl) (session: UserSession) =
   let orgCtx = React.useContext OrgProvider.context
   let orgDispatch = React.useContext OrgProvider.dispatchContext

   classyNode Html.div [ "account-dashboard" ] [
      match url with
      | Routes.AccountUrl.CreateAccount ->
         classyNode Html.article [ "form-wrapper" ] [
            Html.h6 "Create Account"
            CloseButton.render onClose

            AccountCreateFormComponent
               session
               (_.PendingState
                >> AccountProfile.fromAccount
                >> OrgProvider.Msg.AccountCreated
                >> orgDispatch
                >> onClose)
         ]
         |> ScreenOverlay.Portal
      | _ -> ()

      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "title-and-button-container" ] [
            Html.h4 "Accounts"
            Html.button [
               attr.children [
                  Fa.i [ Fa.Solid.Plus ] []
                  Html.span "Create Account"
               ]

               attr.onClick (fun _ ->
                  Router.navigate Routes.AccountUrl.CreateAccountPath)
            ]
         ]

         Html.progress [
            match orgCtx with
            | Deferred.Resolved _ -> attr.value 100
            | _ -> ()
         ]

         match orgCtx with
         | Deferred.Resolved(Ok(Some org)) ->
            classyNode Html.div [ "org-summary" ] [
               Html.small "Balance across all accounts: "

               Html.h2 [
                  attr.classes [ "balance" ]
                  attr.text (Money.format org.Balance)
               ]
            ]

            classyNode Html.div [ "grid"; "accounts" ] [
               for account in org.AccountProfiles.Values do
                  Html.article [
                     Html.div [
                        Html.p account.Name
                        Html.h5 [
                           attr.style [ style.margin 0 ]
                           attr.text (Money.format account.Balance)
                        ]
                     ]

                     Html.div [
                        Html.small "Account Number:"
                        AccountNumberComponent account
                     ]

                     Html.div [
                        Html.small "Routing Number:"
                        Html.b (string account.RoutingNumber)
                     ]

                     Html.button [
                        attr.classes [ "outline" ]
                        attr.children [
                           Fa.i [ Fa.Solid.History ] []
                           Html.span "View Transactions"
                        ]

                        attr.onClick (fun _ ->
                           Router.navigate (
                              Routes.TransactionUrl.selectedPath
                                 account.AccountId
                           ))
                     ]
                  ]
            ]
         | _ -> ()
      ]
   ]

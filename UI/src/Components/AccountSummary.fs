module AccountSummary

open Feliz

open Bank.Account.Domain

let render (account: AccountProfile) =
   Html.footer [
      attr.classes [ "container-fluid grid" ]

      attr.children [
         Html.div [
            Html.b [ attr.classes [ "account-name" ]; attr.text account.Name ]

            Html.div [
               Html.p "Balance: "
               Html.ins [ attr.text (Money.format account.Balance) ]
            ]
         ]

         Html.div [
            Html.p "Daily Internal Transfer: "
            Html.ins [
               attr.text (Money.format account.DailyInternalTransferAccrued)
            ]
         ]

         Html.div [
            Html.p "Daily Domestic Transfer: "
            Html.ins [
               attr.text (Money.format account.DailyDomesticTransferAccrued)
            ]
         ]
      ]
   ]

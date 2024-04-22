module AccountSummary

open Feliz

open Bank.Account.Domain

let render (account: AccountState) =
   Html.footer [
      attr.classes [ "container-fluid grid" ]

      attr.children [
         Html.div [
            Html.b [ attr.classes [ "account-name" ]; attr.text account.Name ]

            Html.div [
               Html.p "Balance: "
               Html.ins [ attr.text (money.format account.Balance) ]
            ]
         ]

         Html.div [
            attr.custom (
               "data-tooltip",
               "Monthly maintenance fee is not accrued."
            )
            attr.custom ("data-placement", "top")
            attr.children [
               Html.p "Daily Debit: "
               Html.ins [ attr.text (money.format account.DailyDebitAccrued) ]
               Html.small $"/{money.format account.DailyDebitLimit}"
            ]
         ]

         Html.div [
            Html.p "Daily Internal Transfer: "
            Html.ins [
               attr.text (money.format account.DailyInternalTransferAccrued)
            ]
         ]

         Html.div [
            Html.p "Daily Domestic Transfer: "
            Html.ins [
               attr.text (money.format account.DailyDomesticTransferAccrued)
            ]
         ]
      ]
   ]

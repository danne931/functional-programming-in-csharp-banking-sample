module AccountSummary

open Feliz

open Bank.Account.Domain

let render (account: AccountState) =
   Html.footer [
      attr.classes [ "container-fluid grid" ]

      attr.children [
         Html.b [ attr.classes [ "account-name" ]; attr.text account.Name ]

         Html.div [
            Html.p "Balance: "
            Html.ins [ attr.text (money.format account.Balance) ]
         ]

         Html.div [
            attr.custom (
               "data-tooltip",
               "Monthly maintenance fee is not accrued."
            )
            attr.custom ("data-placement", "top")
            attr.children [
               Html.p "Daily Debit Accrued: "
               Html.ins [ attr.text (money.format account.DailyDebitAccrued) ]
            ]
         ]

         Html.div [
            Html.p "Daily Debit Limit: "
            Html.ins [ attr.text (money.format account.DailyDebitLimit) ]
         ]
      ]
   ]

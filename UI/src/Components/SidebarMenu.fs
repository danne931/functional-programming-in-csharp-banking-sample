module SidebarMenu

open Feliz

type private MenuUrl =
   | Account
   | Employee
   | Card

type private MenuItem = {
   Url: MenuUrl
   SelectedUrl: Routes.IndexUrl
   Name: string
   Href: string
}

let private renderListItem (item: MenuItem) =
   Html.li [
      match item.Url, item.SelectedUrl with
      | Account, Routes.IndexUrl.Account _ -> attr.classes [ "selected" ]
      | Employee, Routes.IndexUrl.Employees _ -> attr.classes [ "selected" ]
      | Card, Routes.IndexUrl.Cards _ -> attr.classes [ "selected" ]
      | _ -> ()

      attr.children [
         Html.a [ attr.text item.Name; attr.href ("#" + item.Href) ]
      ]
   ]

let render (currentUrl: Routes.IndexUrl) =
   classyNode Html.aside [ "menu" ] [
      Html.ul [
         attr.role "listbox"
         attr.children [
            renderListItem {
               Url = Account
               SelectedUrl = currentUrl
               Name = "Accounts"
               Href = Routes.AccountUrl.BasePath
            }

            renderListItem {
               Url = Employee
               SelectedUrl = currentUrl
               Name = "Employees"
               Href = Routes.EmployeeUrl.BasePath
            }

            renderListItem {
               Url = Card
               SelectedUrl = currentUrl
               Name = "Cards"
               Href = Routes.CardUrl.BasePath
            }
         ]
      ]
   ]

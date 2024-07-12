[<RequireQualifiedAccess>]
module EmployeePermissions

open Feliz
open Fable.FontAwesome

open Lib.SharedTypes

let allowed (role: Role) =
   match role with
   | Role.Admin -> [
      "View all bank accounts"
      "Move money"
      "Manage employees"
      "Manage employee cards"
      "Make purchases"
      "Manage transaction notes and categories"
     ]
   | Role.CardOnly -> [
      "Make and view own purchases"
      "Manage transaction notes"
     ]
   | Role.Scholar -> [
      "View all bank accounts"
      "Manage transaction notes and categories"
     ]

let disallowed (role: Role) =
   match role with
   | Role.Admin -> []
   | Role.CardOnly -> [ "Manage employees"; "Move money" ]
   | Role.Scholar -> [ "Manage employees"; "Manage cards"; "Move money" ]

let render (role: Role) =
   React.fragment [
      classyNode Html.div [ "employee-permissions" ] [
         Html.small "Permissions:"
         classyNode Html.ul [ "employee-allowed-feature-access" ] [
            for permission in allowed role ->
               Html.li [
                  Fa.i [ Fa.Solid.CheckCircle ] []
                  Html.span permission
               ]
         ]

         if role <> Role.Admin then
            Html.small "Restrictions:"

            classyNode Html.ul [ "employee-disallowed-feature-access" ] [
               for permission in disallowed role ->
                  Html.li [
                     Fa.i [ Fa.Solid.CheckCircle ] []
                     Html.span permission
                  ]
            ]
      ]
   ]

module Dropdown

open Feliz
open Fable.FontAwesome

type DropdownItem = {
   Text: string
   OnClick: Browser.Types.MouseEvent -> unit
   IsSelected: bool
}

[<RequireQualifiedAccess>]
type DropdownDirection =
   | LTR
   | RTL

   override x.ToString() =
      match x with
      | DropdownDirection.LTR -> "ltr"
      | DropdownDirection.RTL -> "rtl"

[<ReactComponent>]
let DropdownComponent
   (dir: DropdownDirection)
   (showCaret: bool)
   (button: (ReactElement list) option)
   (items: DropdownItem list)
   =
   let isOpen, setIsOpen = React.useState false

   Html.details [
      if not showCaret then
         attr.classes [ "no-caret" ]
      attr.role "list"
      attr.custom ("dir", string dir)
      attr.isOpen isOpen
      attr.onClick (fun e ->
         e.preventDefault ()
         setIsOpen (not isOpen))

      attr.children [
         Html.summary [
            attr.custom ("aria-haspopup", "listbox")
            attr.role "link"
            attr.classes [ "contrast" ]

            attr.children (
               match button with
               | Some button -> button
               | None -> [ Fa.i [ Fa.Solid.EllipsisH ] [] ]
            )
         ]

         Html.ul [
            attr.role "listbox"
            attr.children [
               for item in items ->
                  Html.li [
                     Html.a [
                        attr.href ""
                        attr.text item.Text
                        attr.onClick (fun e ->
                           e.preventDefault ()
                           setIsOpen (not isOpen)
                           item.OnClick e)

                        if item.IsSelected then
                           attr.classes [ "selected" ]
                     ]
                  ]
            ]
         ]
      ]
   ]

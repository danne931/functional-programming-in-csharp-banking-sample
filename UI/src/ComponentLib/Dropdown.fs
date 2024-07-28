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
   (props:
      {|
         Direction: DropdownDirection
         ShowCaret: bool
         Button: (ReactElement list) option
         Items: DropdownItem list
      |})
   =
   let isOpen, setIsOpen = React.useState false

   Html.details [
      if not props.ShowCaret then
         attr.classes [ "no-caret" ]
      attr.role "list"
      attr.custom ("dir", string props.Direction)
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
               match props.Button with
               | Some button -> button
               | None -> [ Fa.i [ Fa.Solid.EllipsisH ] [] ]
            )
         ]

         Html.ul [
            attr.role "listbox"
            attr.children [
               for item in props.Items ->
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

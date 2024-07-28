module FilterPill

open Feliz

let render
   (props:
      {|
         Content: string
         OnSelect: unit -> unit
         OnDelete: unit -> unit
      |})
   =
   Html.button [
      attr.classes [ "outline"; "filter-pill" ]

      attr.children [
         Html.span [
            attr.text props.Content
            attr.onClick (fun _ -> props.OnSelect())
            attr.custom ("data-tooltip", "Edit")
            attr.custom ("data-placement", "bottom")
         ]

         Html.b [
            attr.text "X"
            attr.onClick (fun _ -> props.OnDelete())
            attr.custom ("data-tooltip", "Remove")
            attr.custom ("data-placement", "bottom")
         ]
      ]
   ]

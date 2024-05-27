module FilterPill

open Feliz

let render (content: string) (onSelect: unit -> unit) (onDelete: unit -> unit) =
   Html.button [
      attr.classes [ "outline" ]

      attr.children [
         Html.span [
            attr.text content
            attr.onClick (fun _ -> onSelect ())
            attr.custom ("data-tooltip", "Edit")
            attr.custom ("data-placement", "bottom")
         ]

         Html.b [
            attr.text "X"
            attr.onClick (fun _ -> onDelete ())
            attr.custom ("data-tooltip", "Remove")
            attr.custom ("data-placement", "bottom")
         ]
      ]
   ]

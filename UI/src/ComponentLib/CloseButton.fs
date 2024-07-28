module CloseButton

open Feliz

let render (onClose: Browser.Types.MouseEvent -> unit) =
   Html.button [
      attr.onClick onClose
      attr.text "Close"
      attr.classes [ "secondary"; "outline"; "close" ]
   ]

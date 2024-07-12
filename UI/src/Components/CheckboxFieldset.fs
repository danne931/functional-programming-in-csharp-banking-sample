module CheckboxFieldset

open Feliz

type CheckboxOption<'T> = { Id: 'T; Display: string }

let render
   (props:
      {|
         Options: CheckboxOption<'T> list
         SelectedItems: ('T list) option
         OnChange: ('T list) option -> unit
      |})
   =
   React.fragment [
      for opt in props.Options do
         Html.label [
            Html.input [
               attr.type' "checkbox"
               attr.name (string opt.Id)
               attr.isChecked (
                  match props.SelectedItems with
                  | None -> false
                  | Some ids -> ids |> List.exists (fun id -> id = opt.Id)
               )

               attr.onChange (fun (_: Browser.Types.Event) ->
                  let selected =
                     match props.SelectedItems with
                     | Some ids ->
                        let withoutClicked =
                           ids |> List.filter (fun id -> id <> opt.Id)

                        if withoutClicked.Length = ids.Length then
                           Some(opt.Id :: ids)
                        else if not withoutClicked.IsEmpty then
                           Some withoutClicked
                        else
                           None
                     | None -> Some [ opt.Id ]

                  props.OnChange selected)
            ]

            Html.text opt.Display
         ]
   ]

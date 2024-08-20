module TableControlPanel

open Feliz
open Fable.FontAwesome
open System

type FilterPillConfig<'FilterView> = {
   View: 'FilterView
   OnDelete: unit -> unit
   Content: string option
}

[<ReactComponent>]
let TableControlPanelComponent
   (props:
      {|
         FilterViewOptions: ('FilterView * string) list
         FilterPills: FilterPillConfig<'FilterView> list
         RenderFilterViewOnSelect: 'FilterView -> ReactElement
         SubsequentChildren: ReactElement list option
      |})
   =
   let filterView, setFilterView = React.useState<'FilterView option> None

   let toggleFilterView () =
      match filterView with
      | None -> props.FilterViewOptions |> List.tryHead |> Option.map fst
      | Some _ -> None
      |> setFilterView

   React.fragment [
      classyNode Html.div [ "control-panel" ] [
         Html.button [
            attr.children [ Fa.i [ Fa.Solid.Filter ] []; Html.span "Filter" ]

            attr.onClick (fun e ->
               e.preventDefault ()
               toggleFilterView ())
         ]


         for config in props.FilterPills do
            match config.Content with
            | None -> ()
            | Some content ->
               FilterPill.render {|
                  Content = content
                  OnSelect = fun () -> setFilterView (Some config.View)
                  OnDelete = config.OnDelete
               |}

         match props.SubsequentChildren with
         | Some children -> React.keyedFragment (Guid.NewGuid(), children)
         | None -> ()
      ]

      match filterView with
      | None -> ()
      | Some selectedView ->
         Html.div [
            attr.classes [ "grid"; "filter-view" ]

            attr.onKeyDown (fun e ->
               e.stopPropagation ()

               if e.key = "Escape" then
                  toggleFilterView ())

            attr.children [
               Html.aside [
                  CloseButton.render (fun _ -> toggleFilterView ())

                  Html.ul [
                     for view, name in props.FilterViewOptions ->
                        Html.li [
                           if selectedView = view then
                              attr.classes [ "active" ]

                           attr.children [
                              Html.a [
                                 attr.href "#"
                                 attr.text name
                                 attr.classes [ "secondary" ]

                                 attr.onClick (fun e ->
                                    e.preventDefault ()

                                    if selectedView <> view then
                                       setFilterView (Some view))

                                 if selectedView = view then
                                    attr.ariaDisabled true
                                    attr.classes [ "primary" ]
                                    attr.style [ style.cursor.defaultCursor ]
                              ]
                           ]
                        ]
                  ]
               ]

               Html.section [ props.RenderFilterViewOnSelect selectedView ]
            ]
         ]
   ]

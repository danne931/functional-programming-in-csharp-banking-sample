namespace Fable.Form.Simple.Pico

[<RequireQualifiedAccess>]
module Form =
   module View =
      open Feliz

      open Fable.Form
      open Fable.Form.Simple.Form.View
      open Fable.FontAwesome

      let group (fields: ReactElement list) =
         Html.div [
            attr.classes [ "grid" ]
            fields
            |> List.mapi (fun index field ->
               Html.div [ attr.key index; attr.children [ field ] ])
            |> attr.children
         ]

      let fieldLabel (label: string) = Html.label [ Html.text label ]

      let errorMessage (message: string) = Html.small [ attr.text message ]

      let errorMessageAsHtml (showError: bool) (error: Error.Error option) =
         match error with
         | Some(Error.External externalError) -> errorMessage externalError
         | _ ->
            if showError then
               error
               |> Option.map (errorToString >> errorMessage)
               |> Option.defaultValue Html.none
            else
               Html.none

      let withLabelAndError
         (label: string)
         (showError: bool)
         (error: Error.Error option)
         (fieldAsHtml: ReactElement)
         : ReactElement
         =
         React.fragment [
            fieldLabel label
            fieldAsHtml
            errorMessageAsHtml showError error
         ]

      let inputField
         (typ: InputType)
         ({
             Dispatch = dispatch
             OnChange = onChange
             OnBlur = onBlur
             Disabled = disabled
             Value = value
             Error = error
             ShowError = showError
             Attributes = attributes
          }: TextFieldConfig<'Msg, IReactProperty>)
         =
         let inputType =
            match typ with
            | Text -> "text"
            | Password -> "password"
            | Email -> "email"
            | Number -> "number"
            | _ -> failwith "Not implemented"

         Html.input [
            attr.onChange (onChange >> dispatch)

            match onBlur with
            | Some onBlur -> attr.onBlur (fun _ -> dispatch onBlur)
            | None -> ()

            attr.type' inputType
            attr.disabled disabled
            attr.value value
            attr.placeholder attributes.Placeholder
            attr.ariaLabel attributes.Label
            if showError && error.IsSome then
               attr.ariaInvalid true

            match typ with
            | Email -> attr.autoComplete "email"
            | _ -> ()

            yield! attributes.HtmlAttributes
         ]
         |> withLabelAndError attributes.Label showError error

      [<ReactComponent>]
      let DateFieldComponent
         ({
             Dispatch = dispatch
             OnChange = onChange
             OnBlur = onBlur
             Disabled = _
             Value = value
             Error = error
             ShowError = _
             Attributes = attributes
          }: TextFieldConfig<'Msg, IReactProperty>)
         =
         let onChange = fst >> onChange >> dispatch
         let onValidDate = React.useCallbackRef onChange

         Html.div [
            attr.onBlur (fun _ -> onBlur |> Option.iter dispatch)

            attr.children [
               fieldLabel attributes.Label

               CustomDateInput.DateInputComponent {|
                  InitialInput = value
                  OnValidDate = onValidDate
                  ExternalError = error |> Option.map errorToString
               |}
            ]
         ]

      let selectField
         ({
             Dispatch = dispatch
             OnChange = onChange
             OnBlur = onBlur
             Disabled = disabled
             Value = value
             Error = error
             ShowError = showError
             Attributes = attributes
          }: SelectFieldConfig<'Msg>)
         =
         let toOption (key: string, label: string) =
            Html.option [
               attr.value key
               attr.text label
               if key = value then
                  attr.disabled true
            ]

         Html.select [
            attr.disabled disabled
            attr.onChange (onChange >> dispatch)

            match onBlur with
            | Some onBlur -> attr.onBlur (fun _ -> dispatch onBlur)
            | None -> ()

            attr.value value

            attr.children [
               if attributes.Placeholder.Length > 0 then
                  Html.option [
                     attr.disabled true
                     attr.value ""
                     attr.text ("-- " + attributes.Placeholder + " --")
                  ]

               yield! attributes.Options |> List.map toOption
            ]

            if showError && error.IsSome then
               attr.ariaInvalid true
         ]
         |> withLabelAndError attributes.Label showError error

      let checkboxField
         ({
             Dispatch = dispatch
             OnChange = onChange
             OnBlur = onBlur
             Disabled = disabled
             Value = value
             Attributes = attributes
          }: CheckboxFieldConfig<'Msg>)
         =
         classyNode Html.div [ "grid" ] [
            Html.label [ Html.text attributes.Text ]

            Html.div [
               Html.input [
                  attr.type' "checkbox"
                  attr.role "switch"
                  attr.disabled disabled
                  attr.isChecked value
                  attr.onCheckedChange (onChange >> dispatch)
               ]
            ]

            Html.div []
         ]

      let radioField
         ({
             Dispatch = dispatch
             OnChange = onChange
             OnBlur = onBlur
             Disabled = disabled
             Value = value
             Error = error
             ShowError = showError
             Attributes = attributes
          }: RadioFieldConfig<'Msg>)
         =
         let radio (key: string, label: string) =
            Html.div [
               Html.input [
                  attr.type' "radio"
                  attr.disabled disabled
                  attr.isChecked ((key = value))
                  attr.onChange (fun (_: bool) -> onChange key |> dispatch)
                  match onBlur with
                  | Some onBlur -> attr.onBlur (fun _ -> dispatch onBlur)
                  | None -> ()
               ]

               Html.label [ attr.text label ]
            ]

         Html.div [
            if showError && error.IsSome then
               attr.ariaInvalid true

            attr.children (attributes.Options |> List.map radio)
         ]
         |> withLabelAndError attributes.Label showError error

      let formList
         ({
             Dispatch = dispatch
             Forms = forms
             Label = label
             Add = add
             Disabled = disabled
          }: FormListConfig<'Msg>)
         =
         let addButton =
            match disabled, add with
            | (false, Some add) ->
               Html.button [
                  attr.onClick (fun _ -> add.Action() |> dispatch)

                  attr.children [
                     Fa.i [ Fa.Solid.Plus ] []

                     Html.span add.Label
                  ]
               ]
            | _ -> Html.none

         classyNode Html.div [ "form-list-container" ] [
            Html.div [
               fieldLabel label

               yield! forms

               addButton
            ]
         ]

      let formListItem
         ({
             Dispatch = dispatch
             Fields = fields
             Delete = delete
             Disabled = disabled
          }: FormListItemConfig<'Msg>)
         =
         let removeButton =
            match disabled, delete with
            | (false, Some delete) ->
               Html.button [
                  attr.onClick (fun _ -> delete.Action() |> dispatch)

                  attr.children [
                     Fa.i [ Fa.Solid.Trash ] []

                     if delete.Label <> "" then
                        Html.span delete.Label
                  ]
               ]
            | _ -> Html.none

         classyNode Html.div [ "form-list"; "grid" ] [
            for field in fields do
               Html.div [ field ]

            Html.div [
               // NOTE:
               // Ghost label ensures the button stays aligned with the previous
               // elements in case one of them displays a validation error.
               Html.label [
                  attr.text "g"
                  attr.style [ style.visibility.hidden ]
               ]
               removeButton
            ]
         ]

      let submitButton (buttonText: string) (state: State) =
         Html.button [
            attr.type' "submit"
            match state with
            | State.Loading ->
               attr.text "Processing..."
               attr.ariaBusy true
            | _ -> attr.text buttonText
         ]

      let secondaryButton (txt: string) (onClick: unit -> unit) (state: State) =
         Html.button [
            attr.text txt
            attr.classes [ "secondary"; "outline" ]
            attr.onClick (fun e ->
               e.preventDefault ()
               onClick ())

            match state with
            | State.Loading -> attr.ariaBusy true
            | _ -> ()
         ]

      let cancelButton = secondaryButton "Cancel"

      let backButton = secondaryButton "Go Back"

      let submitAndCancelButton buttonText onCancel state =
         group [ cancelButton onCancel state; submitButton buttonText state ]

      let submitAndBackButton buttonText onBack state =
         group [ backButton onBack state; submitButton buttonText state ]

      let form
         ({
             Dispatch = dispatch
             OnSubmit = onSubmit
             State = state
             Action = action
             Fields = fields
          }: FormConfig<'Msg>)
         =
         Html.form [
            attr.onSubmit (fun e ->
               e.preventDefault ()
               e.stopPropagation ()

               onSubmit |> Option.map dispatch |> Option.defaultWith ignore)

            attr.children [
               Html.fieldSet [
                  match state with
                  | Loading -> attr.disabled true
                  | _ -> ()

                  attr.children [
                     yield! fields

                     classyNode Html.div [ "form-submit-controls" ] [
                        match action with
                        | Action.SubmitOnly buttonText ->
                           submitButton buttonText state
                        | Action.Custom customAction ->
                           customAction state dispatch
                     ]
                  ]
               ]
            ]
         ]

      // Contract that we need to implement to support
      // the features offered by Fable.Simple.Form.
      let htmlViewConfig<'Msg> : CustomConfig<'Msg, IReactProperty> = {
         Form = form
         TextField = inputField InputType.Text
         NumberField = inputField InputType.Number
         EmailField = inputField InputType.Email
         SelectField = selectField
         CheckboxField = checkboxField
         Group = group
         PasswordField = fun _ -> Html.none
         TextAreaField = fun _ -> Html.none
         ColorField = fun _ -> Html.none
         DateField = DateFieldComponent
         DateTimeLocalField = fun _ -> Html.none
         SearchField = fun _ -> Html.none
         TelField = fun _ -> Html.none
         TimeField = fun _ -> Html.none
         RadioField = radioField
         Section = fun _ _ -> Html.none
         FormList = formList
         FormListItem = formListItem
         FileField = fun _ -> Html.none
      }

      // Function which will be called by the consumer to render the form
      let asHtml (config: ViewConfig<'Values, 'Msg>) =
         custom htmlViewConfig config

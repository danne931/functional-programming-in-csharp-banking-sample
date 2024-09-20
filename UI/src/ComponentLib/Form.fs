namespace Fable.Form.Simple.Pico

[<RequireQualifiedAccess>]
module Form =
   module View =
      open Feliz

      open Fable.Form
      open Fable.Form.Simple.Form.View

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

      let submitButton (buttonText: string) (state: State) =
         Html.button [
            attr.type' "submit"
            match state with
            | State.Loading ->
               attr.text "Processing..."
               attr.ariaBusy true
            | _ -> attr.text buttonText
         ]

      let cancelButton (onCancel: unit -> unit) (state: State) =
         Html.button [
            attr.text "Cancel"
            attr.classes [ "secondary" ]
            attr.onClick (fun e ->
               e.preventDefault ()
               onCancel ())

            match state with
            | State.Loading -> attr.ariaBusy true
            | _ -> ()
         ]

      let submitAndCancelButton buttonText onCancel state =
         group [ cancelButton onCancel state; submitButton buttonText state ]

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
         RadioField = fun _ -> Html.none
         Section = fun _ _ -> Html.none
         FormList = fun _ -> Html.none
         FormListItem = fun _ -> Html.none
         FileField = fun _ -> Html.none
      }

      // Function which will be called by the consumer to render the form
      let asHtml (config: ViewConfig<'Values, 'Msg>) =
         custom htmlViewConfig config

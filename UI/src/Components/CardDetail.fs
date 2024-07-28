module CardDetail

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open System
open Fable.FontAwesome
open Elmish.SweetAlert

open Bank.Account.Domain
open UIDomain.Account
open UIDomain.Employee
open Bank.Employee.Domain
open Lib.SharedTypes
open Dropdown
open Bank.Employee.Forms.DailyPurchaseLimitForm
open Bank.Employee.Forms.MonthlyPurchaseLimitForm

type State = {
   NicknamePersistence: Deferred<Result<EmployeeCommandReceipt, Err>>
   LockPersistence: Deferred<Result<EmployeeCommandReceipt, Err>>
   IsEditingNickname: bool
   IsEditingDailyPurchaseLimit: bool
   IsEditingMonthlyPurchaseLimit: bool
}

type NicknameEditMsg = {
   CommandInput: CardNicknamed
   Employee: Employee
   InitiatedBy: InitiatedById
}

type CardLockMsg = {
   WillLock: bool
   Employee: Employee
   Card: Card
   InitiatedBy: InitiatedById
}

type Msg =
   | ToggleNicknameEdit
   | ToggleDailyPurchaseLimitEdit
   | ToggleMonthlyPurchaseLimitEdit
   | SaveNickname of
      NicknameEditMsg *
      AsyncOperationStatus<Result<EmployeeCommandReceipt, Err>>
   | ShowCardLockConfirmation of CardLockMsg
   | ConfirmUpdateLock of
      CardLockMsg *
      AsyncOperationStatus<Result<EmployeeCommandReceipt, Err>>
   | DismissUpdateLock

let init () =
   {
      NicknamePersistence = Deferred.Idle
      LockPersistence = Deferred.Idle
      IsEditingNickname = false
      IsEditingDailyPurchaseLimit = false
      IsEditingMonthlyPurchaseLimit = false
   },
   Cmd.none

let update (notifyParentOnUpdate: EmployeeCommandReceipt -> unit) msg state =
   match msg with
   | ToggleNicknameEdit ->
      {
         state with
            IsEditingNickname = not state.IsEditingNickname
      },
      Cmd.none
   | ToggleDailyPurchaseLimitEdit ->
      {
         state with
            IsEditingDailyPurchaseLimit = not state.IsEditingDailyPurchaseLimit
            IsEditingMonthlyPurchaseLimit = false
      },
      Cmd.none
   | ToggleMonthlyPurchaseLimitEdit ->
      {
         state with
            IsEditingMonthlyPurchaseLimit =
               not state.IsEditingMonthlyPurchaseLimit
            IsEditingDailyPurchaseLimit = false
      },
      Cmd.none
   | SaveNickname(edit, Started) ->
      let command =
         EditCardNicknameCommand.create
            edit.Employee.CompositeId
            edit.InitiatedBy
            edit.CommandInput
         |> EmployeeCommand.EditCardNickname

      let submitCommand = async {
         let! res = EmployeeService.submitCommand edit.Employee command
         return Msg.SaveNickname(edit, Finished res)
      }

      {
         state with
            NicknamePersistence = Deferred.InProgress
      },
      Cmd.fromAsync submitCommand
   | SaveNickname(_, Finished(Ok receipt)) ->
      notifyParentOnUpdate receipt

      {
         state with
            IsEditingNickname = false
            NicknamePersistence = Deferred.Idle
      },
      Cmd.none
   | SaveNickname(_, Finished(Error err)) ->
      {
         state with
            NicknamePersistence = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err
   | ShowCardLockConfirmation msg ->
      let lockOrUnlock = if msg.WillLock then "lock" else "unlock"

      let confirm =
         ConfirmAlert(
            $"This will {lockOrUnlock} {msg.Card.Display} for {msg.Employee.Name}",
            function
            | ConfirmAlertResult.Confirmed ->
               Msg.ConfirmUpdateLock(msg, Started)
            | ConfirmAlertResult.Dismissed _ -> Msg.DismissUpdateLock
         )
            .Title($"Are you sure you want to {lockOrUnlock} this card?")
            .Type(AlertType.Question)
            .ShowCloseButton(true)

      state, SweetAlert.Run confirm
   | DismissUpdateLock -> state, Cmd.none
   | ConfirmUpdateLock(msg, Started) ->
      let command =
         if msg.WillLock then
            LockCardCommand.create msg.Employee.CompositeId msg.InitiatedBy {
               CardId = msg.Card.CardId
               CardNumberLast4 = msg.Card.CardNumberLast4
               Reference = None
            }
            |> EmployeeCommand.LockCard
         else
            UnlockCardCommand.create msg.Employee.CompositeId msg.InitiatedBy {
               CardId = msg.Card.CardId
               CardNumberLast4 = msg.Card.CardNumberLast4
               Reference = None
            }
            |> EmployeeCommand.UnlockCard

      let submitCommand = async {
         let! res = EmployeeService.submitCommand msg.Employee command
         return Msg.ConfirmUpdateLock(msg, Finished res)
      }

      {
         state with
            LockPersistence = Deferred.InProgress
      },
      Cmd.fromAsync submitCommand
   | ConfirmUpdateLock(_, Finished(Ok receipt)) ->
      notifyParentOnUpdate receipt

      {
         state with
            IsEditingDailyPurchaseLimit = false
            IsEditingMonthlyPurchaseLimit = false
            LockPersistence = Deferred.Idle
      },
      Cmd.none
   | ConfirmUpdateLock(_, Finished(Error err)) ->
      {
         state with
            LockPersistence = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err

let private menuButton (onClick: _ -> unit) (children: ReactElement list) =
   Html.div [
      attr.role "button"
      attr.classes [ "outline" ]
      attr.children children
      attr.onClick onClick
   ]

let private nicknameCancelButton dispatch =
   Html.a [
      attr.href ""
      attr.text "Cancel"
      attr.style [ style.padding 10 ]
      attr.classes [ "secondary" ]
      attr.onClick (fun e ->
         e.preventDefault ()
         dispatch Msg.ToggleNicknameEdit)
   ]

let private nicknameSaveButton onClick =
   Html.a [
      attr.href ""
      attr.text "Save"
      attr.style [ style.padding 10 ]
      attr.onClick (fun e ->
         e.preventDefault ()
         onClick ())
   ]

[<ReactComponent>]
let CardNicknameComponent
   (session: UserSession)
   (card: CardWithMetrics)
   dispatch
   =
   let nickname = card.Card.CardNickname
   let pendingNickname, setNickname = React.useState nickname

   let nicknameInputRef = React.useInputRef ()

   React.useEffectOnce (fun () ->
      match nicknameInputRef.current with
      | None -> ()
      | Some input -> input.focus ())

   classyNode Html.div [ "nickname" ] [
      Html.input [
         attr.ref nicknameInputRef
         attr.ariaLabel "Card Nickname"
         attr.type' "text"
         attr.placeholder "Edit card nickname"

         attr.value (pendingNickname |> Option.defaultValue "")

         attr.onChange (fun input ->
            if String.IsNullOrWhiteSpace input then None else Some input
            |> setNickname)
      ]

      if pendingNickname <> nickname then
         match pendingNickname with
         | None -> Html.small $"Card nickname ({nickname}) unchanged."
         | Some alias ->
            Html.small $"Card nickname ({nickname}) will display as {alias}."

      classyNode Html.div [ "nickname-controls" ] [
         match pendingNickname with
         | Some alias when pendingNickname <> nickname ->
            if pendingNickname <> nickname then
               nicknameCancelButton dispatch

               nicknameSaveButton (fun () ->
                  let msg =
                     Msg.SaveNickname(
                        {
                           CommandInput = {
                              Name = alias
                              PriorName = nickname
                              CardId = card.Card.CardId
                           }
                           Employee = card.Employee
                           InitiatedBy = (InitiatedById session.EmployeeId)

                        },
                        Started
                     )

                  dispatch msg)
         | _ -> ()
      ]
   ]

[<ReactComponent>]
let CardDetailComponent
   (userSession: UserSession)
   (card: CardWithMetrics)
   (notifyParentOnUpdate: EmployeeCommandReceipt -> unit)
   =
   let state, dispatch =
      React.useElmish (init, update notifyParentOnUpdate, [||])

   let accounts =
      OrgAndAccountProfileProvider.context
      |> React.useContext
      |> _.AccountProfiles

   let lockCardMsg = {
      InitiatedBy = InitiatedById userSession.EmployeeId
      Employee = card.Employee
      Card = card.Card
      WillLock = true
   }

   classyNode Html.div [ "card-detail" ] [
      Html.small card.Employee.Name
      Html.p card.Card.Display

      if state.IsEditingNickname then
         CardNicknameComponent userSession card dispatch

      classyNode Html.div [ "spending-container" ] [
         Html.small "Daily spending"

         Html.b
            $"{Money.format card.DailyPurchaseAccrued} of {Money.format card.Card.DailyPurchaseLimit}"

         Html.progress [
            card.DailyPurchaseAccrued / card.Card.DailyPurchaseLimit
            |> string
            |> attr.value
         ]

         if state.IsEditingDailyPurchaseLimit then
            DailyPurchaseLimitFormComponent
               userSession
               (fun receipt ->
                  notifyParentOnUpdate receipt
                  dispatch Msg.ToggleDailyPurchaseLimitEdit)
               card.Card
               card.Employee
      ]

      classyNode Html.div [ "spending-container" ] [
         Html.small "Monthly spending"

         Html.b
            $"{Money.format card.MonthlyPurchaseAccrued} of {Money.format card.Card.MonthlyPurchaseLimit}"

         Html.progress [
            card.MonthlyPurchaseAccrued / card.Card.MonthlyPurchaseLimit
            |> string
            |> attr.value
         ]

         if state.IsEditingMonthlyPurchaseLimit then
            MonthlyPurchaseLimitFormComponent
               userSession
               (fun receipt ->
                  notifyParentOnUpdate receipt
                  dispatch Msg.ToggleMonthlyPurchaseLimitEdit)
               card.Card
               card.Employee
      ]

      classyNode Html.div [ "grid"; "card-detail-menu" ] [
         match card.Card.Status with
         | CardStatus.Active ->
            menuButton
               (fun _ -> dispatch <| Msg.ShowCardLockConfirmation lockCardMsg)
               [ Html.span [ Fa.i [ Fa.Solid.Key ] [] ]; Html.span "Lock Card" ]
         | CardStatus.Frozen ->
            menuButton
               (fun _ ->
                  dispatch
                  <| Msg.ShowCardLockConfirmation {
                     lockCardMsg with
                        WillLock = false
                  })
               [
                  Html.span [ Fa.i [ Fa.Solid.Key ] [] ]
                  Html.span "Unlock Card"
               ]
         | CardStatus.Closed -> ()

         Html.div [
            attr.role "button"
            attr.classes [ "outline" ]
            attr.children [
               Html.span [ Fa.i [ Fa.Solid.History ] [] ]
               Html.span "Transactions"
            ]
            attr.onClick (fun _ ->
               let pathArray =
                  Routes.AccountUrl.selectedPath card.Card.AccountId

               let queryString =
                  {
                     AccountBrowserQuery.empty with
                        SelectedCards =
                           Some [
                              {
                                 Display = card.Card.Display
                                 CardId = card.Card.CardId
                              }
                           ]
                  }
                  |> AccountBrowserQuery.toQueryParams
                  |> Router.encodeQueryString

               Router.navigate [| yield! pathArray; queryString |])
         ]
      ]

      DropdownComponent {|
         Direction = DropdownDirection.LTR
         ShowCaret = true
         Button = None
         Items = [
            {
               Text = "Edit Nickname"
               OnClick = fun _ -> dispatch Msg.ToggleNicknameEdit
               IsSelected = state.IsEditingNickname
            }
            {
               Text = "Edit Daily Purchase Limit"
               OnClick = fun _ -> dispatch Msg.ToggleDailyPurchaseLimitEdit
               IsSelected = state.IsEditingDailyPurchaseLimit
            }
            {
               Text = "Edit Monthly Purchase Limit"
               OnClick = fun _ -> dispatch Msg.ToggleMonthlyPurchaseLimitEdit
               IsSelected = state.IsEditingMonthlyPurchaseLimit
            }
         (*
            {
               Text = "Deactivate Card"
               OnClick = fun _ -> ()
               IsSelected = false
            }
            *)
         ]
      |}

      classyNode Html.div [ "card-summary" ] [
         classyNode Html.div [ "grid" ] [
            Html.small "Account"
            Html.p (
               match accounts with
               | Deferred.Resolved(Ok(Some profiles)) ->
                  profiles.TryFind card.Card.AccountId
                  |> Option.map _.Name
                  |> Option.defaultValue "-"
               | _ -> "-"
            )
         ]

         classyNode Html.div [ "grid" ] [
            Html.small "Card type"
            Html.p (
               if card.Card.Virtual then
                  $"Virtual {card.Card.CardType}"
               else
                  $"Physical {card.Card.CardType}"
            )
         ]
      ]
   ]

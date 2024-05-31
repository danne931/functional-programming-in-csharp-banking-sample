module TransactionDetail

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open Fable.FontAwesome
open System

open Bank.Account.Domain
open Bank.Account.UIDomain
open Bank.Transfer.Domain
open AsyncUtil
open Lib.SharedTypes

type private TransactionMaybe =
   Deferred<Result<TransactionWithAncillaryInfo, Err>>

type State = {
   TransactionId: EventId
   Transaction: TransactionMaybe
   // Nickname may refer to transfer recipient or merchant
   // depending on the AccountEvent rendered.
   EditingNickname: bool
   NicknamePersistence: Deferred<Result<EventId, Err>>
}

let private updateTransaction
   (state: State)
   (transform: TransactionWithAncillaryInfo -> TransactionWithAncillaryInfo)
   =
   {
      state with
         Transaction = (Deferred.map << Result.map) transform state.Transaction
   }

type Msg =
   | GetTransactionInfo of
      AsyncOperationStatus<Result<TransactionWithAncillaryInfo, Err>>
   | SaveCategory of
      TransactionCategory option *
      AsyncOperationStatus<Result<int, Err>>
   | SaveNote of note: string * AsyncOperationStatus<Result<int, Err>>
   | ToggleNicknameEdit
   | SaveRecipientNickname of
      Account *
      TransferRecipient *
      nickname: string option *
      AsyncOperationStatus<Result<EventId, Err>>
   | SaveMerchantNickname of Merchant * AsyncOperationStatus<Result<int, Err>>

let init txnId () =
   {
      TransactionId = txnId
      Transaction = Deferred.Idle
      EditingNickname = false
      NicknamePersistence = Deferred.Idle
   },
   Cmd.ofMsg (GetTransactionInfo Started)

let update (merchantDispatch: MerchantProvider.Dispatch) msg state =
   match msg with
   | GetTransactionInfo Started ->
      let getInfo = async {
         let! res = TransactionService.getTransactionInfo state.TransactionId

         return GetTransactionInfo(Finished res)
      }

      state, Cmd.fromAsync getInfo
   | GetTransactionInfo(Finished(Ok res)) ->
      {
         state with
            Transaction = Deferred.Resolved(Ok res)
      },
      Cmd.none
   | GetTransactionInfo(Finished(Error err)) ->
      Log.error $"Error fetching ancillary txn info: {err}"

      {
         state with
            Transaction = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err
   | SaveCategory(category, Started) ->
      let updateCategory = async {
         let! res =
            match category with
            | None -> TransactionService.deleteCategory state.TransactionId
            | Some category ->
               TransactionService.updateCategory state.TransactionId category.Id

         return SaveCategory(category, Finished res)
      }

      state, Cmd.fromAsync updateCategory
   | SaveCategory(category, Finished(Ok _)) ->
      updateTransaction state (fun txn -> { txn with Category = category }),
      Cmd.none
   | SaveCategory(_, Finished(Error err)) ->
      Log.error $"Error selecting txn category: {err}"
      state, Alerts.toastCommand err
   | SaveNote(note, Started) ->
      let updateNote = async {
         let! res = TransactionService.updateNote state.TransactionId note

         return SaveNote(note, Finished res)
      }

      state, Cmd.fromAsync updateNote
   | SaveNote(note, Finished(Ok _)) ->
      updateTransaction state (fun txn -> { txn with Note = Some note }),
      Cmd.none
   | SaveNote(_, Finished(Error err)) ->
      Log.error $"Error updating note: {err}"
      state, Alerts.toastCommand err
   | ToggleNicknameEdit ->
      {
         state with
            EditingNickname = not state.EditingNickname
      },
      Cmd.none
   | SaveRecipientNickname(account, recipient, nickname, Started) ->
      let command =
         NicknameRecipientCommand.create account.CompositeId {
            Nickname = nickname
            Recipient = recipient
         }
         |> AccountCommand.NicknameRecipient

      let submitCommand = async {
         let! res = AccountService.submitCommand account command

         return
            Msg.SaveRecipientNickname(
               account,
               recipient,
               nickname,
               Finished res
            )
      }

      {
         state with
            NicknamePersistence = Deferred.InProgress
      },
      Cmd.fromAsync submitCommand
   | SaveRecipientNickname(_, _, _, Finished(Ok _)) ->
      {
         state with
            EditingNickname = false
            NicknamePersistence = Deferred.Idle
      },
      Cmd.none
   | SaveRecipientNickname(_, _, _, Finished(Error err)) ->
      {
         state with
            NicknamePersistence = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err
   | SaveMerchantNickname(merchant, Started) ->
      let updateMerchant = async {
         let! res = TransactionService.updateMerchant merchant
         return SaveMerchantNickname(merchant, Finished res)
      }

      {
         state with
            NicknamePersistence = Deferred.InProgress
      },
      Cmd.fromAsync updateMerchant
   | SaveMerchantNickname(merchant, Finished(Ok _)) ->
      // Update parent context of merchant aliases via context reducer.
      merchantDispatch (MerchantProvider.Action.UpdateMerchantAlias merchant)

      {
         state with
            EditingNickname = false
            NicknamePersistence = Deferred.Idle
      },
      Cmd.none
   | SaveMerchantNickname(_, Finished(Error err)) ->
      {
         state with
            NicknamePersistence = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err

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

let private nicknameSaveButton dispatch msg =
   Html.a [
      attr.href ""
      attr.text "Save"
      attr.style [ style.padding 10 ]
      attr.onClick (fun e ->
         e.preventDefault ()
         dispatch msg)
   ]

let private small (text: string) =
   Html.small [ attr.style [ style.marginBottom 0 ]; attr.text text ]

[<ReactComponent>]
let RecipientNicknameEditComponent
   (account: Account)
   (recipient: TransferRecipient)
   dispatch
   =
   let recipientNickname =
      account.TransferRecipients
      |> Map.tryFind recipient.LookupKey
      |> Option.bind _.Nickname

   let pendingNickname, setNickname = React.useState recipientNickname

   let nicknameInputRef = React.useInputRef ()

   React.useEffectOnce (fun () ->
      match nicknameInputRef.current with
      | None -> ()
      | Some input -> input.focus ())

   Html.div [
      Html.input [
         attr.ref nicknameInputRef
         attr.ariaLabel "Transfer Recipient Nickname"
         attr.type' "text"
         attr.placeholder "Edit recipient nickname"

         attr.value (pendingNickname |> Option.defaultValue "")

         attr.onChange (fun input ->
            if String.IsNullOrWhiteSpace input then None else Some input
            |> setNickname)
      ]

      if pendingNickname = (Some recipient.Name) then
         small $"No change from original name {recipient.Name}."
      elif pendingNickname <> recipientNickname then
         match pendingNickname with
         | None -> small $"Transactions will display as {recipient.Name}."
         | Some name ->
            small $"Transactions for {recipient.Name} will display as {name}."

      Html.div [
         attr.style [ style.textAlign.right ]

         attr.children [
            if pendingNickname = (Some recipient.Name) then
               nicknameCancelButton dispatch
            elif pendingNickname <> recipientNickname then
               nicknameCancelButton dispatch

               nicknameSaveButton
               <| dispatch
               <| Msg.SaveRecipientNickname(
                  account,
                  recipient,
                  pendingNickname,
                  Started
               )
         ]
      ]
   ]

[<ReactComponent>]
let MerchantNicknameEditComponent
   (debit: BankEvent<DebitedAccount>)
   (merchants: Map<string, Merchant>)
   dispatch
   =
   let debitOrigin = debit.Data.Origin

   let merchantAlias =
      merchants |> Map.tryFind (debitOrigin.ToLower()) |> Option.bind _.Alias

   let pendingNickname, setNickname = React.useState merchantAlias

   let nicknameInputRef = React.useInputRef ()

   React.useEffectOnce (fun () ->
      match nicknameInputRef.current with
      | None -> ()
      | Some input -> input.focus ())

   Html.div [
      Html.input [
         attr.ref nicknameInputRef
         attr.ariaLabel "Merchant Recipient Nickname"
         attr.type' "text"
         attr.placeholder "Edit merchant nickname"

         attr.value (pendingNickname |> Option.defaultValue "")

         attr.onChange (fun input ->
            if String.IsNullOrWhiteSpace input then None else Some input
            |> setNickname)
      ]

      if pendingNickname = (Some debitOrigin) then
         small $"No change from original name {debitOrigin}."
      elif pendingNickname <> merchantAlias then
         match pendingNickname with
         | None ->
            small
               $"Transactions will display with the original name {debitOrigin}."
         | Some name ->
            small $"Transactions for {debitOrigin} will display as {name}."

      Html.div [
         attr.style [ style.textAlign.right ]

         attr.children [
            if pendingNickname = (Some debitOrigin) then
               nicknameCancelButton dispatch
            elif pendingNickname <> merchantAlias then
               nicknameCancelButton dispatch

               nicknameSaveButton
               <| dispatch
               <| Msg.SaveMerchantNickname(
                  {
                     OrgId = debit.OrgId
                     Name = debitOrigin.ToLower()
                     Alias = pendingNickname
                  },
                  Started
               )
         ]
      ]
   ]

let renderTransactionInfo
   (account: Account)
   (txnInfo: TransactionWithAncillaryInfo)
   (isEditingNickname: bool)
   (merchants: Map<string, Merchant>)
   dispatch
   =
   let txn = transactionUIFriendly account txnInfo.Event

   React.fragment [
      Html.h6 txn.Name

      Html.section [
         Html.h3 [
            attr.text (string txn.Amount)
            attr.style [ style.margin 0 ]
         ]

         Html.small txn.Date
      ]

      Html.section [
         Html.div [
            Html.small "From:"
            Html.h6 [
               attr.style [ style.display.inlineBlock; style.marginLeft 10 ]
               attr.text account.Name
               attr.text txn.Source
            ]
         ]

         match txnInfo.Event with
         | AccountEvent.TransferPending e when isEditingNickname ->
            RecipientNicknameEditComponent account e.Data.Recipient dispatch
         | AccountEvent.DebitedAccount e when isEditingNickname ->
            MerchantNicknameEditComponent e merchants dispatch
         | _ ->
            let txn =
               eventWithMerchantAlias txnInfo.Event merchants
               |> transactionUIFriendly account

            Html.div [
               Html.small "To:"
               Html.h6 [
                  attr.style [ style.display.inlineBlock; style.marginLeft 10 ]
                  attr.text txn.Destination
               ]
            ]
      ]
   ]

let renderCategorySelect
   (categories: Map<int, TransactionCategory>)
   (txnInfo: TransactionMaybe)
   dispatch
   =
   React.fragment [
      Html.label [ Html.text "Category" ]
      Html.select [
         attr.onChange (fun (catId: string) ->
            Msg.SaveCategory(Map.tryFind (int catId) categories, Started)
            |> dispatch)

         match txnInfo with
         | Deferred.Resolved(Ok txnInfo) ->
            attr.children [
               Html.option [ attr.value 0; attr.text "None" ]

               for category in categories.Values do
                  Html.option [
                     attr.value category.Id
                     attr.text category.Name

                     match txnInfo.Category with
                     | Some cat when cat.Id = category.Id -> attr.selected true
                     | _ -> ()
                  ]
            ]
         | _ -> attr.disabled true
      ]
   ]

let renderNoteInput (txnInfo: TransactionMaybe) dispatch =
   React.fragment [
      Html.label [ Html.text "Notes" ]
      Html.input [
         attr.type' "text"
         attr.placeholder "Add a note"

         match txnInfo with
         | Deferred.Resolved(Ok txnInfo) ->
            attr.key (string txnInfo.Id)

            attr.defaultValue (txnInfo.Note |> Option.defaultValue "")
         | _ -> attr.disabled true

         attr.onChange (
            throttleUncontrolledInput 2500 (fun note ->
               SaveNote(note, Started) |> dispatch)
         )
      ]
   ]

let renderFooterMenuControls
   (txnInfo: TransactionMaybe)
   (isEditingNickname: bool)
   dispatch
   =
   let evtOpt =
      match txnInfo with
      | Deferred.Resolved(Ok txnInfo) ->
         match txnInfo.Event with
         | AccountEvent.TransferPending _
         | AccountEvent.TransferDeposited _
         | AccountEvent.DebitedAccount _ -> Some txnInfo.Event
         | _ -> None
      | _ -> None

   React.fragment [
      match evtOpt with
      | None -> ()
      | Some evt ->
         Html.details [
            attr.role "list"
            attr.custom ("dir", "rtl")

            attr.children [
               Html.summary [
                  attr.custom ("aria-haspopup", "listbox")
                  attr.role "link"
                  attr.classes [ "contrast" ]

                  attr.children [ Fa.i [ Fa.Solid.EllipsisH ] [] ]
               ]

               Html.ul [
                  attr.id "accounts-list"
                  attr.role "listbox"
                  attr.children [
                     match evt with
                     | AccountEvent.DebitedAccount _ ->
                        Html.li [
                           Html.a [
                              attr.href ""
                              attr.text "Edit merchant nickname"
                              if isEditingNickname then
                                 attr.classes [ "selected" ]

                              attr.onClick (fun e ->
                                 e.preventDefault ()
                                 dispatch ToggleNicknameEdit)
                           ]
                        ]
                     | AccountEvent.TransferPending _ ->
                        Html.li [
                           Html.a [
                              attr.href ""
                              attr.text "Nickname Recipient"
                              if isEditingNickname then
                                 attr.classes [ "selected" ]

                              attr.onClick (fun e ->
                                 e.preventDefault ()
                                 dispatch ToggleNicknameEdit)
                           ]
                        ]

                        Html.li [
                           Html.a [
                              attr.onClick (fun e -> e.preventDefault ())
                              attr.text "View Recipient"
                              attr.href ""
                           ]
                        ]
                     | AccountEvent.TransferDeposited evt ->
                        Html.li [
                           Html.a [
                              attr.onClick (fun e -> e.preventDefault ())
                              attr.text "Nickname Sender"
                              attr.href ""
                           ]
                        ]

                        Html.li [
                           Html.a [
                              attr.onClick (fun e -> e.preventDefault ())
                              attr.text "View Sender"
                              attr.href ""
                           ]
                        ]
                     | _ -> ()
                  ]
               ]
            ]
         ]
   ]

[<ReactComponent>]
let TransactionDetailComponent (account: Account) (txnId: EventId) =
   let merchants = React.useContext MerchantProvider.stateContext
   let merchantDispatchCtx = React.useContext MerchantProvider.dispatchContext

   let state, dispatch =
      React.useElmish (init txnId, update merchantDispatchCtx, [| box txnId |])

   let categories = React.useContext TransactionCategoryProvider.context
   let browserQuery = Routes.IndexUrl.accountBrowserQuery ()

   classyNode Html.div [ "transaction-detail" ] [
      CloseButton.render (fun _ ->
         let queryString =
            { browserQuery with Transaction = None }
            |> AccountBrowserQuery.toQueryParams
            |> Router.encodeQueryString

         Router.navigate ("account", string account.EntityId, queryString))

      match state.Transaction with
      | Deferred.Resolved(Ok txn) ->
         renderTransactionInfo
            account
            txn
            state.EditingNickname
            merchants
            dispatch
      | _ -> Html.div [ attr.ariaBusy true ]

      Html.section [
         renderCategorySelect categories state.Transaction dispatch
         renderNoteInput state.Transaction dispatch
      ]

      Html.section [
         attr.style [ style.position.relative ]

         attr.children [
            renderFooterMenuControls
               state.Transaction
               state.EditingNickname
               dispatch
         ]
      ]
   ]

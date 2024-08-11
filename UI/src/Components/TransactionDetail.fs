module TransactionDetail

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open System

open Bank.Account.Domain
open Bank.Employee.Domain
open UIDomain.Account
open Bank.Transfer.Domain
open Lib.SharedTypes
open Dropdown

type private TransactionMaybe =
   Deferred<Result<TransactionWithAncillaryInfo option, Err>>

/// Is the transaction detail component implemented for a given AccountEvent
let hasRenderImplementation =
   function
   | AccountEvent.DepositedCash _
   | AccountEvent.InternalTransferPending _
   | AccountEvent.DomesticTransferPending _
   | AccountEvent.DomesticTransferRejected _
   | AccountEvent.TransferDeposited _
   | AccountEvent.DebitedAccount _ -> true
   | _ -> false

type State = {
   TransactionId: EventId
   Transaction: TransactionMaybe
   // Nickname may refer to transfer recipient or merchant
   // depending on the AccountEvent rendered.
   EditingNickname: bool
   NicknamePersistence: Deferred<Result<EventId, Err>>
   // Sometimes the user clicks on a persisted event in the table
   // before the read model is synced, resulting in a 404 and
   // nothing to display in this view.  Reattempt fetching the
   // transaction info if this is the case.
   GetTxnAttempt: int
}

let private updateTransaction
   (state: State)
   (transform: TransactionWithAncillaryInfo -> TransactionWithAncillaryInfo)
   =
   {
      state with
         Transaction =
            (Deferred.map << Result.map << Option.map)
               transform
               state.Transaction
   }

type RecipientNicknameEditMsg = {
   CommandInput: RecipientNicknamed
   Account: Account
   InitiatedBy: InitiatedById
}

type Msg =
   | GetTransactionInfo of
      AsyncOperationStatus<Result<TransactionWithAncillaryInfo option, Err>>
   | SaveCategory of
      TransactionCategory option *
      AsyncOperationStatus<Result<int, Err>>
   | SaveNote of note: string * AsyncOperationStatus<Result<int, Err>>
   | ToggleNicknameEdit
   | SaveRecipientNickname of
      RecipientNicknameEditMsg *
      AsyncOperationStatus<Result<AccountCommandReceipt, Err>>
   | SaveMerchantNickname of Merchant * AsyncOperationStatus<Result<int, Err>>
   | EditTransferRecipient of senderId: AccountId * recipientId: AccountId

let init txnId () =
   {
      TransactionId = txnId
      Transaction = Deferred.Idle
      EditingNickname = false
      NicknamePersistence = Deferred.Idle
      GetTxnAttempt = 1
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
   | GetTransactionInfo(Finished(Ok(Some res))) ->
      {
         state with
            Transaction = res |> Some |> Ok |> Deferred.Resolved
      },
      Cmd.none
   | GetTransactionInfo(Finished(Error err)) ->
      Log.error $"Error fetching ancillary txn info: {err}"

      {
         state with
            Transaction = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err
   | GetTransactionInfo(Finished(Ok None)) ->
      let attempt = state.GetTxnAttempt

      if attempt <= 4 then
         let delayedReattempt = async {
            do! Async.Sleep(1500 * attempt)
            Log.info $"Reattempt fetching transaction info {attempt}."
            return GetTransactionInfo Started
         }

         {
            state with
               GetTxnAttempt = attempt + 1
         },
         Cmd.fromAsync delayedReattempt
      else
         let err = Err.NetworkError(exn "Unable to resolve transaction.")

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
   | SaveRecipientNickname(edit, Started) ->
      let command =
         NicknameRecipientCommand.create
            edit.Account.CompositeId
            edit.InitiatedBy
            edit.CommandInput
         |> AccountCommand.NicknameRecipient

      let submitCommand = async {
         let! res = AccountService.submitCommand edit.Account command
         return Msg.SaveRecipientNickname(edit, Finished res)
      }

      {
         state with
            NicknamePersistence = Deferred.InProgress
      },
      Cmd.fromAsync submitCommand
   | SaveRecipientNickname(_, Finished(Ok _)) ->
      {
         state with
            EditingNickname = false
            NicknamePersistence = Deferred.Idle
      },
      Cmd.none
   | SaveRecipientNickname(_, Finished(Error err)) ->
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
   | EditTransferRecipient(senderId, recipientId) ->
      let browserQuery = Routes.IndexUrl.accountBrowserQuery ()

      let queryString =
         {
            browserQuery with
               Transaction = None
               Action =
                  Some(AccountActionView.EditTransferRecipient recipientId)
         }
         |> AccountBrowserQuery.toQueryParams
         |> Router.encodeQueryString

      state,
      Cmd.navigate (
         Routes.TransactionUrl.BasePath,
         string senderId,
         queryString
      )

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
let RecipientNicknameEditComponent
   (session: UserSession)
   (account: Account)
   dispatch
   (recipientId: AccountId)
   (recipientEnv: RecipientAccountEnvironment)
   =
   let name, nickname = nameAndNicknamePair account recipientId
   let pendingNickname, setNickname = React.useState nickname

   let nicknameInputRef = React.useInputRef ()

   React.useEffectOnce (fun () ->
      match nicknameInputRef.current with
      | None -> ()
      | Some input -> input.focus ())

   classyNode Html.div [ "nickname" ] [
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

      if pendingNickname = (Some name) then
         Html.small $"No change from original name {name}."
      elif pendingNickname <> nickname then
         match pendingNickname with
         | None ->
            Html.small
               $"Transactions will display as {name} for past and future transactions."
         | Some alias ->
            Html.small
               $"Transactions for {name} will display as {alias} for past and future transactions."

      classyNode Html.div [ "nickname-controls" ] [
         if pendingNickname = (Some name) then
            nicknameCancelButton dispatch
         elif pendingNickname <> nickname then
            nicknameCancelButton dispatch

            nicknameSaveButton (fun () ->
               let msg =
                  Msg.SaveRecipientNickname(
                     {
                        CommandInput = {
                           RecipientId = recipientId
                           RecipientAccountEnvironment = recipientEnv
                           Nickname = pendingNickname
                        }
                        Account = account
                        InitiatedBy = (InitiatedById session.EmployeeId)

                     },
                     Started
                  )

               dispatch msg)
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

   classyNode Html.div [ "nickname" ] [
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
         Html.small $"No change from original name {debitOrigin}."
      elif pendingNickname <> merchantAlias then
         match pendingNickname with
         | None ->
            Html.small
               $"Transactions will display with the original name {debitOrigin} for past and future transactions."
         | Some name ->
            Html.small
               $"Transactions for {debitOrigin} will display as {name} for past and future transactions."

      classyNode Html.div [ "nickname-controls" ] [
         if pendingNickname = (Some debitOrigin) then
            nicknameCancelButton dispatch
         elif pendingNickname <> merchantAlias then
            nicknameCancelButton dispatch

            nicknameSaveButton (fun () ->
               let msg =
                  Msg.SaveMerchantNickname(
                     {
                        OrgId = debit.OrgId
                        Name = debitOrigin.ToLower()
                        Alias = pendingNickname
                     },
                     Started
                  )

               dispatch msg)
      ]
   ]

let renderTransactionInfo
   (account: Account)
   (txnInfo: TransactionWithAncillaryInfo)
   (isEditingNickname: bool)
   (merchants: Map<string, Merchant>)
   (session: UserSession)
   dispatch
   =
   let txn = transactionUIFriendly account txnInfo.Event

   let RecipientNicknameEditComponent =
      RecipientNicknameEditComponent session account dispatch

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
         match txn.Source with
         | Some source ->
            Html.div [
               Html.small "From:"
               Html.h6 [
                  attr.style [ style.display.inlineBlock; style.marginLeft 10 ]
                  attr.text account.Name
                  attr.text source
               ]
            ]
         | None -> ()

         match txnInfo.Event with
         | AccountEvent.InternalTransferRecipient e when isEditingNickname ->
            RecipientNicknameEditComponent
               e.Data.Recipient.AccountId
               RecipientAccountEnvironment.Internal
         | AccountEvent.InternalTransferPending e when isEditingNickname ->
            RecipientNicknameEditComponent
               e.Data.BaseInfo.RecipientId
               RecipientAccountEnvironment.Internal
         | AccountEvent.DomesticTransferRecipient e when isEditingNickname ->
            RecipientNicknameEditComponent
               e.Data.Recipient.AccountId
               RecipientAccountEnvironment.Domestic
         | AccountEvent.DomesticTransferPending e when isEditingNickname ->
            RecipientNicknameEditComponent
               e.Data.BaseInfo.Recipient.AccountId
               RecipientAccountEnvironment.Domestic
         | AccountEvent.DomesticTransferRejected e when isEditingNickname ->
            RecipientNicknameEditComponent
               e.Data.BaseInfo.Recipient.AccountId
               RecipientAccountEnvironment.Domestic
         | AccountEvent.DebitedAccount e when isEditingNickname ->
            MerchantNicknameEditComponent e merchants dispatch
         | _ ->
            let txn =
               eventWithMerchantAlias txnInfo.Event merchants
               |> transactionUIFriendly account

            match txn.Destination with
            | Some destination ->
               Html.div [
                  Html.small "To:"
                  Html.h6 [
                     attr.style [
                        style.display.inlineBlock
                        style.marginLeft 10
                     ]
                     attr.text destination
                  ]
               ]
            | None -> ()
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
         | Deferred.Resolved(Ok(Some txnInfo)) ->
            txnInfo.Category
            |> Option.map _.Id
            |> Option.defaultValue 0
            |> attr.value

            attr.children [
               Html.option [ attr.value 0; attr.text "None" ]

               for category in categories.Values do
                  Html.option [
                     attr.value category.Id
                     attr.text category.Name
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
         | Deferred.Resolved(Ok(Some txnInfo)) ->
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
   (account: Account)
   (txnInfo: TransactionMaybe)
   (isEditingNickname: bool)
   dispatch
   =
   let evtOpt =
      match txnInfo with
      | Deferred.Resolved(Ok(Some txnInfo)) ->
         match txnInfo.Event with
         | AccountEvent.InternalTransferPending _
         | AccountEvent.DomesticTransferPending _
         | AccountEvent.DomesticTransferRejected _
         | AccountEvent.DomesticTransferRecipient _
         | AccountEvent.TransferDeposited _
         | AccountEvent.DebitedAccount _ -> Some txnInfo.Event
         | _ -> None
      | _ -> None

   React.fragment [
      match evtOpt with
      | None -> ()
      | Some evt ->
         DropdownComponent {|
            Direction = DropdownDirection.RTL
            ShowCaret = false
            Button = None
            Items =
               match evt with
               | AccountEvent.DebitedAccount _ -> [
                  {
                     Text = "Edit merchant nickname"
                     OnClick = fun _ -> dispatch ToggleNicknameEdit
                     IsSelected = isEditingNickname
                  }
                 ]
               | AccountEvent.InternalTransferPending _
               | AccountEvent.DomesticTransferPending _
               | AccountEvent.InternalTransferRecipient _
               | AccountEvent.DomesticTransferRecipient _
               | AccountEvent.DomesticTransferRejected _
               | AccountEvent.EditedDomesticTransferRecipient _ ->
                  [
                     {
                        Text = "Nickname recipient"
                        OnClick = fun _ -> dispatch ToggleNicknameEdit
                        IsSelected = isEditingNickname
                     }

                  ]
                  @ match canEditTransferRecipient account evt with
                    | None -> []
                    | Some recipient -> [
                       {
                          Text = "Edit recipient"
                          OnClick =
                             fun _ ->
                                dispatch (
                                   Msg.EditTransferRecipient(
                                      account.AccountId,
                                      recipient.AccountId
                                   )
                                )
                          IsSelected = isEditingNickname
                       }
                      ]
               | AccountEvent.TransferDeposited evt -> [
                  {
                     Text = "Nickname sender"
                     OnClick = fun _ -> ()
                     IsSelected = false
                  }
                  {
                     Text = "View sender"
                     OnClick = fun _ -> ()
                     IsSelected = false
                  }
                 ]
               | _ -> []
         |}
   ]

[<ReactComponent>]
let TransactionDetailComponent
   (session: UserSession)
   (account: Account)
   (txnId: EventId)
   =
   let merchants = React.useContext MerchantProvider.stateContext
   let merchantDispatchCtx = React.useContext MerchantProvider.dispatchContext

   let state, dispatch =
      React.useElmish (init txnId, update merchantDispatchCtx, [| box txnId |])

   let categories = React.useContext TransactionCategoryProvider.context

   classyNode Html.article [ "transaction-detail" ] [
      CloseButton.render (fun _ ->
         let browserQuery = Routes.IndexUrl.accountBrowserQuery ()

         let queryString =
            { browserQuery with Transaction = None }
            |> AccountBrowserQuery.toQueryParams
            |> Router.encodeQueryString

         Router.navigate (
            Routes.TransactionUrl.BasePath,
            string account.AccountId,
            queryString
         ))

      match state.Transaction with
      | Deferred.Resolved(Ok(Some txn)) ->
         renderTransactionInfo
            account
            txn
            state.EditingNickname
            merchants
            session
            dispatch
      | Deferred.Resolved(Ok None) -> Html.p "No transaction found."
      | _ -> Html.progress []

      Html.section [
         renderCategorySelect categories state.Transaction dispatch
         renderNoteInput state.Transaction dispatch
      ]

      Html.section [
         attr.style [ style.position.relative ]

         attr.children [
            renderFooterMenuControls
               account
               state.Transaction
               state.EditingNickname
               dispatch
         ]
      ]
   ]

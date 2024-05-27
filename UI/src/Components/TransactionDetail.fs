module TransactionDetail

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open Fable.FontAwesome
open System

open Bank.Account.Domain
open Bank.Account.UIDomain
open AsyncUtil
open Lib.SharedTypes

type State = {
   TransactionId: Guid
   Transaction: Deferred<TransactionWithAncillaryInfo>
}

type Msg =
   | GetTransactionInfo of
      AsyncOperationStatus<Result<TransactionWithAncillaryInfo, Err>>
   | CategorySelected of
      TransactionCategory option *
      AsyncOperationStatus<Result<int, Err>>
   | NoteUpdated of note: string * AsyncOperationStatus<Result<int, Err>>

let init txnId () =
   {
      TransactionId = txnId
      Transaction = Deferred.Idle
   },
   Cmd.ofMsg (GetTransactionInfo Started)

let update msg state =
   match msg with
   | GetTransactionInfo Started ->
      let getInfo = async {
         let! res = TransactionService.getTransactionInfo state.TransactionId

         return GetTransactionInfo(Finished res)
      }

      state, Cmd.fromAsync getInfo
   | GetTransactionInfo(Finished(Ok txn)) ->
      {
         state with
            Transaction = Deferred.Resolved txn
      },
      Cmd.none
   | GetTransactionInfo(Finished(Error err)) ->
      Log.error $"Error fetching ancillary txn info: {err}"
      state, Cmd.none
   | CategorySelected(category, Started) ->
      let updateCategory = async {
         let! res =
            match category with
            | None -> TransactionService.deleteCategory state.TransactionId
            | Some category ->
               TransactionService.updateCategory state.TransactionId category.Id

         return CategorySelected(category, Finished res)
      }

      state, Cmd.fromAsync updateCategory
   | CategorySelected(category, Finished(Ok _)) ->
      {
         state with
            Transaction =
               state.Transaction
               |> Deferred.map (fun txn -> { txn with Category = category })
      },
      Cmd.none
   | CategorySelected(_, Finished(Error err)) ->
      Log.error $"Error selecting txn category: {err}"
      state, Cmd.none
   | NoteUpdated(note, Started) ->
      let updateNote = async {
         let! res = TransactionService.updateNote state.TransactionId note

         return NoteUpdated(note, Finished res)
      }

      state, Cmd.fromAsync updateNote
   | NoteUpdated(note, Finished(Ok _)) ->
      {
         state with
            Transaction =
               state.Transaction
               |> Deferred.map (fun txn -> { txn with Note = Some note })
      },
      Cmd.none
   | NoteUpdated(_, Finished(Error err)) ->
      Log.error $"Error updating note: {err}"
      state, Cmd.none

let renderTransactionInfo
   (profile: AccountProfile)
   (txnInfo: TransactionWithAncillaryInfo)
   =
   let txn = transactionUIFriendly profile txnInfo.Event

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
               attr.text profile.Name
               attr.text txn.Source
            ]
         ]

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
   (txnInfo: Deferred<TransactionWithAncillaryInfo>)
   dispatch
   =
   React.fragment [
      Html.label [ Html.text "Category" ]
      Html.select [
         attr.onChange (fun (catId: string) ->
            Msg.CategorySelected(Map.tryFind (int catId) categories, Started)
            |> dispatch)

         match txnInfo with
         | Deferred.Resolved txnInfo ->
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

let renderNoteInput (txnInfo: Deferred<TransactionWithAncillaryInfo>) dispatch =
   React.fragment [
      Html.label [ Html.text "Notes" ]
      Html.input [
         attr.type' "text"
         attr.placeholder "Add a note"

         match txnInfo with
         | Deferred.Resolved txnInfo ->
            attr.key txnInfo.Id

            attr.defaultValue (txnInfo.Note |> Option.defaultValue "")
         | _ -> attr.disabled true

         attr.onChange (
            throttleUncontrolledInput 2500 (fun note ->
               NoteUpdated(note, Started) |> dispatch)
         )
      ]
   ]

let renderFooterMenuControls (txnInfo: Deferred<TransactionWithAncillaryInfo>) =
   let evtOpt =
      match txnInfo with
      | Deferred.Resolved txnInfo ->
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
                     | AccountEvent.DebitedAccount evt ->
                        Html.li [
                           Html.a [
                              attr.onClick (fun e -> e.preventDefault ())
                              attr.text "Edit merchant nickname"
                              attr.href ""
                           ]
                        ]
                     | AccountEvent.TransferPending evt ->
                        Html.li [
                           Html.a [
                              attr.onClick (fun e -> e.preventDefault ())
                              attr.text "Nickname Recipient"
                              attr.href ""
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
let TransactionDetailComponent (profile: AccountProfile) (txnId: Guid) =
   let state, dispatch = React.useElmish (init txnId, update, [| box txnId |])
   let categories = React.useContext Contexts.transactionCategoryContext
   let browserQuery = Routes.IndexUrl.accountBrowserQuery ()

   classyNode Html.div [ "transaction-detail" ] [
      CloseButton.render (fun _ ->
         let queryString =
            { browserQuery with Transaction = None }
            |> AccountBrowserQuery.toQueryParams
            |> Router.encodeQueryString

         Router.navigate ("account", string profile.EntityId, queryString))

      match state.Transaction with
      | Deferred.Resolved transaction ->
         renderTransactionInfo profile transaction
      | _ -> Html.div [ attr.ariaBusy true ]

      Html.section [
         renderCategorySelect categories state.Transaction dispatch
         renderNoteInput state.Transaction dispatch
      ]

      Html.section [
         attr.style [ style.position.relative ]

         attr.children [ renderFooterMenuControls state.Transaction ]
      ]
   ]

module TransactionDetail

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open Fable.FontAwesome
open Fable
open System

open Bank.Account.Domain
open Bank.Account.UIDomain
open AsyncUtil
open Lib.SharedTypes

type State = {
   AccountId: Guid
   Transaction: AncillaryTransactionInfo
}

type Msg =
   | GetAncillaryTransactionInfo of
      txnId: Guid *
      AsyncOperationStatus<Result<AncillaryTransactionInfo, Err>>
   | CategorySelected of
      category: TransactionCategory *
      AsyncOperationStatus<Result<int, Err>>
   | NoteUpdated of note: string * AsyncOperationStatus<Result<int, Err>>
   | Close

let init accountId transactionId () =
   {
      AccountId = accountId
      Transaction = {
         Id = transactionId
         Note = None
         Category = None
      }
   },
   Cmd.ofMsg (GetAncillaryTransactionInfo(transactionId, Started))

let update msg state =
   match msg with
   | GetAncillaryTransactionInfo(txnId, Started) ->
      let getInfo = async {
         let! res =
            AncillaryTransactionInfoService.getAncillaryTransactionInfo txnId

         return GetAncillaryTransactionInfo(txnId, Finished res)
      }

      state, Cmd.fromAsync getInfo
   | GetAncillaryTransactionInfo(txnId, Finished(Ok txn)) ->
      { state with Transaction = txn }, Cmd.none
   | GetAncillaryTransactionInfo(txnId, Finished(Error err)) ->
      Log.error $"Error fetching ancillary txn info: {err}"
      state, Cmd.none
   | CategorySelected(category, Started) ->
      let updateCategory = async {
         let! res =
            AncillaryTransactionInfoService.updateCategory
               state.Transaction.Id
               category.Id

         return CategorySelected(category, Finished res)
      }

      state, Cmd.fromAsync updateCategory
   | CategorySelected(category, Finished(Ok _)) ->
      {
         state with
            Transaction.Category = Some category
      },
      Cmd.none
   | CategorySelected(_, Finished(Error err)) ->
      Log.error $"Error selecting txn category: {err}"
      state, Cmd.none
   | NoteUpdated(note, Started) ->
      let updateNote = async {
         let! res =
            AncillaryTransactionInfoService.updateNote state.Transaction.Id note

         return NoteUpdated(note, Finished res)
      }

      state, Cmd.fromAsync updateNote
   | NoteUpdated(note, Finished(Ok _)) ->
      {
         state with
            Transaction.Note = Some note
      },
      Cmd.none
   | NoteUpdated(_, Finished(Error err)) ->
      Log.error $"Error updating note: {err}"
      state, Cmd.none
   | Close -> state, Cmd.navigate ("account", string state.AccountId)

[<ReactComponent>]
let TransactionDetailComponent
   (account: AccountState)
   (transaction: AccountEvent)
   =
   let _, envelope = AccountEnvelope.unwrap transaction
   let txnId = envelope.Id

   let state, dispatch =
      React.useElmish (init account.EntityId txnId, update, [| box txnId |])

   let categories = React.useContext Contexts.transactionCategoryContext

   let txn = transactionUIFriendly account transaction

   classyNode Html.div [ "transaction-detail" ] [
      CloseButton.render (fun _ -> dispatch Close)

      Html.h6 txn.Name

      Html.section [
         Html.h3 [
            attr.text (string txn.Amount)
            attr.style [ style.margin 0 ]
         ]

         Html.small txn.Date
      ]

      Html.section [
         Html.h6 [
            attr.style [ style.margin 0 ]
            attr.text account.Name
            attr.text txn.Source
         ]

         Html.p [ attr.text "--->"; attr.style [ style.margin 0 ] ]

         Html.h6 [ attr.style [ style.margin 0 ]; attr.text txn.Destination ]
      ]

      Html.section [
         Html.label [ Html.text "Category" ]
         Html.select [
            attr.onChange (fun (catId: string) ->
               CategorySelected(categories[int catId], Started) |> dispatch)

            attr.children [
               for category in categories.Values ->
                  Html.option [
                     attr.value category.Id
                     attr.text category.Name

                     match state.Transaction.Category with
                     | Some cat when cat.Id = category.Id -> attr.selected true
                     | _ -> ()
                  ]
            ]
         ]

         Html.label [ Html.text "Notes" ]
         Html.input [
            attr.type' "text"
            attr.placeholder "Add a note"

            attr.defaultValue (state.Transaction.Note |> Option.defaultValue "")

            attr.onChange (
               throttleInput 2500 (fun note ->
                  NoteUpdated(note, Started) |> dispatch)
            )
         ]
      ]

      Html.section [
         attr.style [ style.position.relative ]

         attr.children [
            Html.a [
               attr.href ""
               attr.children [ Fa.i [ Fa.Solid.WindowClose ] [] ]
               attr.onClick (fun e ->
                  e.preventDefault ()
                  dispatch Msg.Close)

               attr.custom ("data-tooltip", "Close")
               attr.custom ("data-placement", "right")
            ]

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
                        Html.li [
                           Html.a [
                              attr.text "Edit merchant nickname"
                              attr.href ""
                              attr.onClick(_.preventDefault())
                           ]
                        ]
                     ]
                  ]
               ]
            ]
         ]
      ]
   ]

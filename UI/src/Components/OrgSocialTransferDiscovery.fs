module OrgSocialTransferDiscovery

open Feliz
open Feliz.UseElmish
open Elmish
open System

open Bank.Account.Domain
open Lib.SharedTypes

type State = {
   SearchInput: string option
   PreviousSearchQuery: string option
   Orgs: Deferred<Result<Org list option, Err>>
}

type Msg =
   | SetSearchInput of string
   | SearchOrgs of
      orgToExclude: OrgId *
      searchQuery: string *
      AsyncOperationStatus<Result<Org list option, Err>>

let init () =
   {
      SearchInput = None
      PreviousSearchQuery = None
      Orgs = Deferred.Idle
   },
   Cmd.none

let update msg state =
   match msg with
   | SetSearchInput str ->
      let state =
         if String.IsNullOrWhiteSpace str then
            {
               state with
                  SearchInput = None
                  PreviousSearchQuery = None
                  Orgs = Deferred.Idle
            }
         else
            { state with SearchInput = Some str }

      state, Cmd.none
   | SearchOrgs(orgId, searchQuery, Started) ->
      if state.PreviousSearchQuery = Some searchQuery then
         state, Cmd.none
      else
         let cmd =
            async {
               let! res =
                  OrgService.searchOrgTransferSocialDiscovery orgId searchQuery

               return SearchOrgs(orgId, searchQuery, Finished res)
            }
            |> Cmd.fromAsync

         {
            state with
               Orgs = Deferred.InProgress
               PreviousSearchQuery = Some searchQuery
         },
         cmd
   | SearchOrgs(_, _, Finished(Ok res)) ->
      {
         state with
            Orgs = Deferred.Resolved(Ok res)
      },
      Cmd.none
   | SearchOrgs(_, _, Finished(Error err)) ->
      Log.error $"Error fetching orgs: {err}"

      {
         state with
            Orgs = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err

[<ReactComponent>]
let OrgSearchComponent
   (orgId: OrgId)
   (makeChildren:
      string option -> Deferred<Result<Org list option, Err>> -> ReactElement)
   =
   let state, dispatch = React.useElmish (init, update, [||])

   let searchRef = React.useInputRef ()

   // Fetch orgs after a debounce.
   React.useEffect (
      fun () ->
         let timer =
            Fable.Core.JS.setTimeout
               (fun () ->
                  match state.SearchInput with
                  | None -> ()
                  | Some searchQuery ->
                     dispatch <| Msg.SearchOrgs(orgId, searchQuery, Started))
               800

         React.createDisposable (fun _ -> Fable.Core.JS.clearTimeout timer)
      , [| box state.SearchInput |]
   )

   React.useEffectOnce (fun () ->
      match searchRef.current with
      | Some searchInput -> searchInput.focus ()
      | None -> ())

   React.fragment [
      Html.input [
         attr.type' "search"
         attr.name $"search-orgs"
         attr.placeholder "Search by organization"
         attr.ariaLabel "Search orgs"
         attr.value (state.SearchInput |> Option.defaultValue "")
         attr.onChange (Msg.SetSearchInput >> dispatch)
         attr.ref searchRef
      ]

      makeChildren state.SearchInput state.Orgs
   ]

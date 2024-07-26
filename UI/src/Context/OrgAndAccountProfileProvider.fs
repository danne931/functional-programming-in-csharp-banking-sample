module OrgAndAccountProfileProvider

open Feliz
open Feliz.UseElmish
open Elmish

open Lib.SharedTypes
open Bank.Account.Domain
open UIDomain.Account

type State = {
   Org: Deferred<OrgMaybe>
   AccountProfiles: Deferred<AccountProfilesMaybe>
}

type Msg =
   | Load of OrgId * AsyncOperationStatus<OrgAndAccountProfilesMaybe>
   | AccountEventPersisted of AccountEventPersistedConfirmation

let private initState = {
   Org = Deferred.Idle
   AccountProfiles = Deferred.Idle
}

let init () = initState, Cmd.none

let update msg state =
   match msg with
   | Load(orgId, Started) ->
      let load = async {
         let! res = AccountService.getOrgAndAccountProfiles orgId
         return Msg.Load(orgId, Finished res)
      }

      {
         state with
            Org = Deferred.InProgress
            AccountProfiles = Deferred.InProgress
      },
      Cmd.fromAsync load
   | Load(_, Finished(Ok(Some(org, profiles)))) ->
      {
         state with
            Org = org |> Some |> Ok |> Deferred.Resolved
            AccountProfiles = profiles |> Some |> Ok |> Deferred.Resolved
      },
      Cmd.none
   | Load _ ->
      Log.error "Issue loading org + account profiles."
      state, Cmd.none
   | AccountEventPersisted confirmation ->
      let account = confirmation.Account
      let accountId = account.AccountId

      let state =
         match state.AccountProfiles with
         | Deferred.Resolved(Ok(Some profiles)) ->
            profiles.TryFind accountId
            |> Option.bind (fun profile ->
               if profile.Balance = account.Balance then
                  None
               else
                  let profile = {
                     profile with
                        Balance = account.Balance
                  }

                  let profile =
                     match confirmation.EventPersisted with
                     | AccountEvent.InternalTransferPending e -> {
                        profile with
                           DailyInternalTransferAccrued =
                              profile.DailyInternalTransferAccrued
                              + e.Data.BaseInfo.Amount
                       }
                     | AccountEvent.DomesticTransferPending e -> {
                        profile with
                           DailyDomesticTransferAccrued =
                              profile.DailyDomesticTransferAccrued
                              + e.Data.BaseInfo.Amount
                       }
                     | AccountEvent.InternalTransferRejected e -> {
                        profile with
                           DailyInternalTransferAccrued =
                              profile.DailyInternalTransferAccrued
                              - e.Data.BaseInfo.Amount
                       }
                     | AccountEvent.DomesticTransferRejected e -> {
                        profile with
                           DailyDomesticTransferAccrued =
                              profile.DailyDomesticTransferAccrued
                              - e.Data.BaseInfo.Amount
                       }
                     | _ -> profile

                  Some {
                     Org = state.Org
                     AccountProfiles =
                        Map.add accountId profile profiles
                        |> Some
                        |> Ok
                        |> Deferred.Resolved
                  })
         | _ -> None
         |> Option.defaultValue state

      state, Cmd.none

let context =
   React.createContext<State> (
      name = "OrgAndAccountProfileContext",
      defaultValue = initState
   )

[<ReactComponent>]
let OrgAndAccountProfileProvider (child: Fable.React.ReactElement) =
   let state, dispatch = React.useElmish (init, update, [||])
   let session = React.useContext UserSessionProvider.context

   React.useEffect (
      fun () ->
         match session with
         | Deferred.Resolved session ->
            dispatch <| Msg.Load(session.OrgId, Started)
         | _ -> ()
      , [| box session |]
   )

   SignalRAccountEventProvider.useAccountEventSubscription {
      ComponentName = "OrgAndAccountProfileContext"
      AccountId = Routes.IndexUrl.accountIdMaybe ()
      OnReceive = Msg.AccountEventPersisted >> dispatch
   }

   React.contextProvider (context, state, child)

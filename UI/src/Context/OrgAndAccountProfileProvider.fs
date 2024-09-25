module OrgProvider

open Feliz
open Feliz.UseElmish
open Elmish

open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Employee.Domain

type State = Deferred<Result<OrgWithAccountProfiles option, Err>>

let updateProfiles
   (state: State)
   (transform: OrgWithAccountProfiles -> OrgWithAccountProfiles)
   : State
   =
   (Deferred.map << Result.map << Option.map) transform state

type Msg =
   | Load of
      OrgId *
      AsyncOperationStatus<Result<OrgWithAccountProfiles option, Err>>
   | BalanceUpdated of
      {|
         AccountId: AccountId
         Balance: decimal
         PersistedEvent: AccountEvent
      |}
   | AccountCreated of Account

let private initState = Deferred.Idle

let init () = initState, Cmd.none

let update msg state =
   match msg with
   | Load(orgId, Started) ->
      let load = async {
         let! res = OrgService.getOrgAndAccountProfiles orgId
         return Msg.Load(orgId, Finished res)
      }

      Deferred.InProgress, Cmd.fromAsync load
   | Load(_, Finished(Ok res)) -> res |> Ok |> Deferred.Resolved, Cmd.none
   | Load(_, Finished(Error err)) ->
      Log.error $"Issue loading org + account profiles. {err}"
      state, Cmd.none
   | BalanceUpdated o ->
      let persistedEvt = o.PersistedEvent
      let balance = o.Balance
      let accountId = o.AccountId

      let transform state =
         state.AccountProfiles
         |> Map.tryFind accountId
         |> Option.map (fun profile ->
            let metrics = profile.Metrics

            let metrics =
               match persistedEvt with
               | AccountEvent.InternalTransferBetweenOrgsPending e -> {
                  metrics with
                     DailyInternalTransferAccrued =
                        metrics.DailyInternalTransferAccrued
                        + e.Data.BaseInfo.Amount
                     MonthlyInternalTransferAccrued =
                        metrics.MonthlyInternalTransferAccrued
                        + e.Data.BaseInfo.Amount
                 }
               | AccountEvent.InternalTransferWithinOrgPending e -> {
                  metrics with
                     DailyInternalTransferAccrued =
                        metrics.DailyInternalTransferAccrued
                        + e.Data.BaseInfo.Amount
                     MonthlyInternalTransferAccrued =
                        metrics.MonthlyInternalTransferAccrued
                        + e.Data.BaseInfo.Amount
                 }
               | AccountEvent.DomesticTransferPending e -> {
                  metrics with
                     DailyDomesticTransferAccrued =
                        metrics.DailyDomesticTransferAccrued
                        + e.Data.BaseInfo.Amount
                     MonthlyDomesticTransferAccrued =
                        metrics.MonthlyDomesticTransferAccrued
                        + e.Data.BaseInfo.Amount
                 }
               | AccountEvent.InternalTransferWithinOrgRejected e -> {
                  metrics with
                     DailyInternalTransferAccrued =
                        metrics.DailyInternalTransferAccrued
                        - e.Data.BaseInfo.Amount
                     MonthlyInternalTransferAccrued =
                        metrics.MonthlyInternalTransferAccrued
                        - e.Data.BaseInfo.Amount
                 }
               | AccountEvent.InternalTransferBetweenOrgsRejected e -> {
                  metrics with
                     DailyInternalTransferAccrued =
                        metrics.DailyInternalTransferAccrued
                        - e.Data.BaseInfo.Amount
                     MonthlyInternalTransferAccrued =
                        metrics.MonthlyInternalTransferAccrued
                        - e.Data.BaseInfo.Amount
                 }
               | AccountEvent.DomesticTransferRejected e -> {
                  metrics with
                     DailyDomesticTransferAccrued =
                        metrics.DailyDomesticTransferAccrued
                        - e.Data.BaseInfo.Amount
                     MonthlyDomesticTransferAccrued =
                        metrics.MonthlyDomesticTransferAccrued
                        - e.Data.BaseInfo.Amount
                 }
               | AccountEvent.DebitedAccount e -> {
                  metrics with
                     DailyPurchaseAccrued =
                        metrics.DailyPurchaseAccrued + e.Data.Amount
                     MonthlyPurchaseAccrued =
                        metrics.MonthlyPurchaseAccrued + e.Data.Amount
                 }
               | _ -> metrics

            let profile = {
               Metrics = metrics
               Account = {
                  profile.Account with
                     Balance = balance
               }
            }

            let profiles = Map.add accountId profile state.AccountProfiles

            {
               Org = state.Org
               AccountProfiles = profiles
               Balance = profiles |> Map.values |> Seq.sumBy _.Account.Balance
            })
         |> Option.defaultValue state

      let state = updateProfiles state transform

      let internalTransferTransform state =
         let internalRecipient =
            match persistedEvt with
            | AccountEvent.InternalTransferWithinOrgPending e ->
               let info = e.Data.BaseInfo

               state.AccountProfiles
               |> Map.tryFind info.Recipient.AccountId
               |> Option.map (fun a ->
                  let account = {
                     a.Account with
                        Balance = a.Account.Balance + info.Amount
                  }

                  { a with Account = account })
            | AccountEvent.InternalTransferWithinOrgRejected e ->
               let info = e.Data.BaseInfo

               state.AccountProfiles
               |> Map.tryFind info.Recipient.AccountId
               |> Option.map (fun a ->
                  let account = {
                     a.Account with
                        Balance = a.Account.Balance - info.Amount
                  }

                  { a with Account = account })
            | _ -> None

         let profiles =
            match internalRecipient with
            | Some profile ->
               Map.add profile.Account.AccountId profile state.AccountProfiles
            | None -> state.AccountProfiles

         {
            Org = state.Org
            AccountProfiles = profiles
            Balance = state.Balance
         }

      let state = updateProfiles state internalTransferTransform

      state, Cmd.none
   | AccountCreated account ->
      let profile = {
         Account = account
         Metrics = {
            DailyInternalTransferAccrued = 0m
            DailyDomesticTransferAccrued = 0m
            MonthlyInternalTransferAccrued = 0m
            MonthlyDomesticTransferAccrued = 0m
            DailyPurchaseAccrued = 0m
            MonthlyPurchaseAccrued = 0m
         }
      }

      updateProfiles state (fun state -> {
         state with
            AccountProfiles =
               state.AccountProfiles |> Map.add account.AccountId profile
      }),
      Cmd.none

let context =
   React.createContext<State> (name = "OrgContext", defaultValue = initState)

let dispatchContext =
   React.createContext<Msg -> unit> (
      name = "OrgDispatchContext",
      defaultValue = ignore
   )

[<ReactComponent>]
let OrgProvider (child: Fable.React.ReactElement) =
   let state, dispatch = React.useElmish (init, update, [||])
   let session = React.useContext UserSessionProvider.context

   React.useEffect (
      fun () ->
         match session with
         | Deferred.Resolved session when state = Deferred.Idle ->
            dispatch <| Msg.Load(session.OrgId, Started)
         | _ -> ()
      , [| box session |]
   )

   React.contextProvider (
      context,
      state,
      React.contextProvider (dispatchContext, dispatch, child)
   )

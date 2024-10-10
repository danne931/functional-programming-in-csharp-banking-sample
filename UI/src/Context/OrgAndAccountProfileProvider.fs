module OrgProvider

open Feliz
open Feliz.UseElmish
open Elmish

open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Employee.Domain
open UIDomain.Account

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
   | AccountCreated of Account
   | AccountUpdated of AccountEventPersistedConfirmation

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
   | AccountUpdated conf ->
      let accountId = conf.Account.AccountId
      let evt = conf.EventPersisted

      let transform state =
         state.AccountProfiles
         |> Map.tryFind accountId
         |> Option.map (fun profile ->
            let metrics = profile.Metrics

            let internalTransferAccrued (amount: decimal) = {
               metrics with
                  DailyInternalTransferAccrued =
                     metrics.DailyInternalTransferAccrued + amount
                  MonthlyInternalTransferAccrued =
                     metrics.MonthlyInternalTransferAccrued + amount
            }

            let domesticTransferAccrued (amount: decimal) = {
               metrics with
                  DailyDomesticTransferAccrued =
                     metrics.DailyDomesticTransferAccrued + amount
                  MonthlyDomesticTransferAccrued =
                     metrics.MonthlyDomesticTransferAccrued + amount
            }

            let metrics =
               match evt with
               | AccountEvent.InternalTransferBetweenOrgsPending e ->
                  internalTransferAccrued e.Data.BaseInfo.Amount
               | AccountEvent.InternalTransferWithinOrgPending e ->
                  internalTransferAccrued e.Data.BaseInfo.Amount
               | AccountEvent.InternalAutomatedTransferPending e ->
                  internalTransferAccrued e.Data.BaseInfo.Amount
               | AccountEvent.DomesticTransferPending e ->
                  domesticTransferAccrued e.Data.BaseInfo.Amount
               | AccountEvent.InternalTransferWithinOrgRejected e ->
                  internalTransferAccrued -e.Data.BaseInfo.Amount
               | AccountEvent.InternalTransferBetweenOrgsRejected e ->
                  internalTransferAccrued -e.Data.BaseInfo.Amount
               | AccountEvent.InternalAutomatedTransferRejected e ->
                  internalTransferAccrued -e.Data.BaseInfo.Amount
               | AccountEvent.DomesticTransferRejected e ->
                  domesticTransferAccrued -e.Data.BaseInfo.Amount
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
               Account = conf.Account
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
         let recipientBalanceUpdate recipientId transferAmount =
            state.AccountProfiles
            |> Map.tryFind recipientId
            |> Option.map (fun a ->
               let account = {
                  a.Account with
                     Balance = a.Account.Balance + transferAmount
               }

               { a with Account = account })

         let recipient =
            match evt with
            | AccountEvent.InternalTransferWithinOrgPending e ->
               recipientBalanceUpdate
                  e.Data.BaseInfo.Recipient.AccountId
                  e.Data.BaseInfo.Amount
            | AccountEvent.InternalAutomatedTransferPending e ->
               recipientBalanceUpdate
                  e.Data.BaseInfo.Recipient.AccountId
                  e.Data.BaseInfo.Amount
            | AccountEvent.InternalTransferWithinOrgRejected e ->
               recipientBalanceUpdate
                  e.Data.BaseInfo.Recipient.AccountId
                  -e.Data.BaseInfo.Amount
            | AccountEvent.InternalAutomatedTransferRejected e ->
               recipientBalanceUpdate
                  e.Data.BaseInfo.Recipient.AccountId
                  -e.Data.BaseInfo.Amount
            | _ -> None

         let profiles =
            match recipient with
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

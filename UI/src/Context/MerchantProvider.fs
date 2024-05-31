module MerchantProvider

open Feliz

open Lib.SharedTypes
open Bank.Account.Domain

type Action =
   | SetMerchants of Map<string, Merchant>
   | UpdateMerchantAlias of Merchant

type Dispatch = Action -> unit

let stateContext =
   React.createContext<Map<string, Merchant>> (
      name = "MerchantContext",
      defaultValue = Map.empty
   )

let dispatchContext =
   React.createContext<Dispatch> (
      name = "MerchantDispatchContext",
      defaultValue = ignore
   )

let reducer (state: Map<string, Merchant>) (action: Action) =
   match action with
   | SetMerchants merchants -> merchants
   | UpdateMerchantAlias merchant ->
      Map.change
         (merchant.Name.ToLower())
         (Option.map (fun o -> { o with Alias = merchant.Alias }))
         state

[<ReactComponent>]
let MerchantProvider (orgId: OrgId) (child: Fable.React.ReactElement) =
   let merchants, dispatch = React.useReducer (reducer, Map.empty)

   React.useEffectOnce (fun () ->
      async {
         match! TransactionService.getMerchants orgId with
         | Ok merchants -> dispatch (SetMerchants merchants)
         | Error err -> Log.error $"Error fetching merchants: {err}"
      }
      |> Async.StartImmediate)

   React.contextProvider (
      stateContext,
      merchants,
      React.contextProvider (dispatchContext, dispatch, child)
   )

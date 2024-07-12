module MerchantProvider

open Feliz

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
      Map.add (merchant.Name.ToLower()) merchant state

[<ReactComponent>]
let MerchantProvider (child: Fable.React.ReactElement) =
   let session = React.useContext UserSessionProvider.context
   let merchants, dispatch = React.useReducer (reducer, Map.empty)

   React.useEffect (
      fun () ->
         match session with
         | Deferred.Resolved session ->
            async {
               match! TransactionService.getMerchants session.OrgId with
               | Ok merchants -> dispatch (SetMerchants merchants)
               | Error err -> Log.error $"Error fetching merchants: {err}"
            }
            |> Async.StartImmediate
         | _ -> ()
      , [| box session |]
   )

   React.contextProvider (
      stateContext,
      merchants,
      React.contextProvider (dispatchContext, dispatch, child)
   )

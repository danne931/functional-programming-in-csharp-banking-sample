module ServiceHealth

open Feliz
open Feliz.UseElmish
open Elmish

open Lib.SharedTypes
open AsyncUtil
open DiagnosticsService

type Msg =
   | Load of AsyncOperationStatus<Result<ServiceDiagnostics, Err>>
   | ServiceHealthEventReceived of CircuitBreakerEvent

type State = {
   DomesticTransfer: ServiceHealth
   Email: ServiceHealth
}

let init () =
   {
      DomesticTransfer = ServiceHealth.Good
      Email = ServiceHealth.Good
   },
   Cmd.ofMsg <| Load Started

let update msg (state: State) =
   match msg with
   | Load Started ->
      let loadInitStatus = async {
         let! res = getServiceHealth ()
         return Load(Finished res)
      }

      state, Cmd.fromAsync loadInitStatus
   | Load(Finished(Ok status)) ->
      {
         state with
            Email = status.Email
            DomesticTransfer = status.DomesticTransfer
      },
      Cmd.none
   | Load(Finished(Error err)) ->
      Log.error $"Error loading service health {err}"
      state, Cmd.none
   | ServiceHealthEventReceived evt ->
      let health = DiagnosticsService.health evt.Status

      let state =
         match evt.Service with
         | CircuitBreakerService.DomesticTransfer -> {
            state with
               DomesticTransfer = health
           }
         | CircuitBreakerService.Email -> { state with Email = health }

      state, Cmd.none

[<ReactComponent>]
let ServiceHealthComponent () =
   let state, dispatch = React.useElmish (init, update, [||])

   let signalRContext = React.useContext SignalRConnectionProvider.context
   let signalRConnection = signalRContext.Connection

   React.useEffect (
      fun () ->
         if signalRConnection.IsSome then
            DiagnosticsService.listenForCircuitBreakerEvent
               (ServiceHealthEventReceived >> dispatch)
               signalRConnection.Value
      , [| box signalRConnection |]
   )

   Html.nav [
      attr.classes [ "container-fluid" ]
      attr.children [
         Html.small [
            Html.span [
               attr.classes [ "system-ops-title" ]
               attr.text "Service Health"
            ]

            Html.span [
               attr.classes [ "system-op" ]
               attr.children [
                  Html.text "Domestic Transfer: "

                  Html.span [
                     attr.classes [
                        if state.DomesticTransfer = ServiceHealth.Good then
                           "success"
                        else
                           "alert"
                     ]
                     attr.text (string state.DomesticTransfer)
                  ]
               ]
            ]

            Html.span [
               attr.classes [ "system-op" ]
               attr.children [
                  Html.text "Email: "

                  Html.span [
                     attr.classes [
                        if state.Email = ServiceHealth.Good then
                           "success"
                        else
                           "alert"
                     ]
                     attr.text (string state.Email)
                  ]
               ]
            ]
         ]
      ]
   ]

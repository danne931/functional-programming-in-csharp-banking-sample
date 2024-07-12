module CardDashboard

open Feliz
open Feliz.UseElmish
open Elmish
open Feliz.Router

open Lib.SharedTypes
open Bank.Employee.Domain
open UIDomain.Employee
open Bank.Employee.Forms
open EmployeeSearch

type State = {
   Cards: Deferred<EmployeeCardPairsMaybe>
}

type Msg =
   | LoadCards of AsyncOperationStatus<EmployeeCardPairsMaybe>
   | EmployeeCommandProcessing of EmployeeCommandReceipt

let init () =
   { Cards = Deferred.Idle }, Cmd.ofMsg (LoadCards Started)

let update (session: UserSession) msg state =
   match msg with
   | LoadCards Started ->
      let load = async {
         do! Async.Sleep 1000
         let err = exn "todo" |> Err.NetworkError |> Error
         return LoadCards(Finished err)
      }

      { Cards = Deferred.InProgress }, Cmd.fromAsync load
   | LoadCards(Finished(Ok(Some cards))) ->
      {
         state with
            Cards = Deferred.Resolved(Ok(Some cards))
      },
      Cmd.none
   | LoadCards(Finished(Ok None)) ->
      {
         state with
            Cards = Deferred.Resolved(Ok None)
      },
      Cmd.none
   | LoadCards(Finished(Error err)) ->
      {
         state with
            Cards = Deferred.Resolved(Error err)
      },
      Cmd.none
   | EmployeeCommandProcessing receipt ->
      let employee = receipt.PendingState

      state, Cmd.none

let private close () = Router.navigate Routes.CardUrl.BasePath

[<ReactComponent>]
let CardDashboardComponent (url: Routes.CardUrl) (session: UserSession) =
   let state, dispatch = React.useElmish (init, update session, [||])

   let onSubmit = Msg.EmployeeCommandProcessing >> dispatch >> close

   classyNode Html.div [ "card-dashboard" ] [
      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [ Html.h5 "Cards" ]

            Html.aside [ CardActionMenu.render () ]

            match url with
            | Routes.CardUrl.CardsWithSearchQuery query ->
               match query.Action with
               | Some view ->

                  classyNode Html.article [ "form-wrapper" ] [
                     Html.h6 (
                        match view with
                        | CardActionView.CardAccess -> "Manage Card Access"
                        | CardActionView.DailyDebitLimit ->
                           "Manage Purchase Limits"
                     )

                     CloseButton.render (fun _ -> close ())

                     match view with
                     | CardActionView.DailyDebitLimit ->
                        EmployeeCardSelectSearchComponent {|
                           OrgId = session.OrgId
                           MakeChildrenOnSelect =
                              Some
                              <| fun card employee -> [
                                 DailyPurchaseLimitForm.DailyPurchaseLimitFormComponent
                                    session
                                    onSubmit
                                    card.CardId
                                    employee
                              ]
                           OnSelect = None
                        |}
                     | CardActionView.CardAccess ->
                        EmployeeCardSelectSearchComponent {|
                           OrgId = session.OrgId
                           MakeChildrenOnSelect =
                              Some
                              <| fun card employee -> [
                                 CardAccess.CardAccessFormComponent
                                    session
                                    onSubmit
                                    card.CardId
                                    employee
                              ]
                           OnSelect = None
                        |}
                  ]
                  |> ScreenOverlay.Portal
               | _ -> ()
            | _ -> ()
         ]
      ]
   ]

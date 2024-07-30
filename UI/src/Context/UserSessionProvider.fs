module UserSessionProvider

open Feliz
open Fable.SimpleHttp

open Bank.Employee.Domain
open RoutePaths

let context =
   React.createContext<Deferred<UserSession>> (
      name = "UserSessionContext",
      defaultValue = Deferred.InProgress
   )

[<ReactComponent>]
let UserSessionProvider (child: Fable.React.ReactElement) =
   let session, setSession = React.useState Deferred.InProgress

   // Mock login to get mock user session properties
   React.useEffectOnce (fun () ->
      async {
         let! code, _ = Http.post UserSessionPath.Login ""

         if code <> 200 then
            Log.error "Issue with mock login"
         else
            let! code, session = Http.get UserSessionPath.GetSession

            if code <> 200 then
               Log.error "Issue getting session"
            else
               let session = Serialization.deserialize<UserSession> session

               match session with
               | Error err -> Log.error (string err)
               | Ok session -> setSession (Deferred.Resolved session)
      }
      |> Async.StartImmediate)

   React.contextProvider (context, session, child)

[<ReactComponent>]
let UserSessionSuspense (childInit: UserSession -> Fable.React.ReactElement) =
   let session = React.useContext context

   match session with
   | Deferred.Resolved session -> childInit session
   | _ -> Html.progress []

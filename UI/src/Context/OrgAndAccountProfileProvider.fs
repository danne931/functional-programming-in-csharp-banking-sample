module OrgAndAccountProfileProvider

open Feliz

open UIDomain.Account

type State = {
   Org: Deferred<OrgMaybe>
   AccountProfiles: Deferred<AccountProfilesMaybe>
}

let private initState = {
   Org = Deferred.Idle
   AccountProfiles = Deferred.Idle
}

let context =
   React.createContext<State> (
      name = "OrgAndAccountProfileContext",
      defaultValue = initState
   )

[<ReactComponent>]
let OrgAndAccountProfileProvider (child: Fable.React.ReactElement) =
   let orgAndProfiles, setOrgAndProfiles = React.useState initState

   let session = React.useContext UserSessionProvider.context

   React.useEffect (
      fun () ->
         match session with
         | Deferred.Resolved session ->
            async {
               match! AccountService.getOrgAndAccountProfiles session.OrgId with
               | Ok(Some(org, profiles)) ->
                  {
                     Org = org |> Some |> Ok |> Deferred.Resolved
                     AccountProfiles =
                        profiles |> Some |> Ok |> Deferred.Resolved
                  }
                  |> setOrgAndProfiles
               | Ok None ->
                  {
                     Org = None |> Ok |> Deferred.Resolved
                     AccountProfiles = None |> Ok |> Deferred.Resolved
                  }
                  |> setOrgAndProfiles
               | Error err ->
                  Log.error $"Error getting org and account profiles: {err}"
            }
            |> Async.StartImmediate
         | _ -> ()
      , [| box session |]
   )

   React.contextProvider (context, orgAndProfiles, child)

[<RequireQualifiedAccess>]
module OrgService

open Fable.SimpleHttp
open FsToolkit.ErrorHandling
open Feliz.Router

open UIDomain.Account
open Bank.Account.Domain
open Lib.SharedTypes
open RoutePaths

let private serviceName = "OrgService"

let getOrgAndAccountProfiles
   (orgId: OrgId)
   : Async<Result<OrgWithAccountProfiles option, Err>>
   =
   async {
      let path = OrgPath.get orgId

      let! (code, responseText) = Http.get path

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         return
            responseText
            |> Serialization.deserialize<OrgWithAccountProfiles>
            |> Result.map Some
   }


let searchOrgTransferSocialDiscovery
   (orgId: OrgId)
   (nameQuery: string)
   : Async<Result<Org list option, Err>>
   =
   async {
      let path = OrgPath.search orgId nameQuery

      let! (code, responseText) = Http.get path

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         return
            responseText
            |> Serialization.deserialize<Org list>
            |> Result.map Some
   }

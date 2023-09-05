module Bank.User.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder

open Bank.User.Api

module private Path =
   let Base = "/users"

let startUserRoutes (app: WebApplication) =
   app.MapGet(
      Path.Base,
      Func<Task<IResult>>(getUsers >> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore

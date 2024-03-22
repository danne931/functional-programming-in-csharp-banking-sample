module Bank.User.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder

open Bank.User.Api
open RoutePaths

let startUserRoutes (app: WebApplication) =
   app.MapGet(
      UserPath.Base,
      Func<Task<IResult>>(getUsers >> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore

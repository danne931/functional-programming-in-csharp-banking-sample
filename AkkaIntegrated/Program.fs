open Microsoft.AspNetCore.Builder

open Bank.Account.Routes
open Bank.Transfer.Routes
open Bank.Hubs

let builder = WebApplication.CreateBuilder()

Config.enableDefaultHttpJsonSerialization builder

Config.startSignalR builder

let actorSystem = Config.startActorModel ()

Config.injectDependencies builder actorSystem

let app = builder.Build()

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore

app.MapHub<AccountHub>("/accountHub") |> ignore

startTransferRoutes app
startAccountRoutes app

app.Run()

type Program() =
   class
   end

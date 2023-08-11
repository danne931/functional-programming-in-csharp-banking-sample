open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting

open Bank.User.Routes
open Bank.Account.Routes
open Bank.Transfer.Routes
open Bank.Hubs

let builder = WebApplication.CreateBuilder()

Config.setEnvironment builder

Config.enableDefaultHttpJsonSerialization builder

Config.startSignalR builder

Config.startActorModel builder

Config.startQuartz builder

Config.injectDependencies builder

let app = builder.Build()

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore

app.MapHub<AccountHub>("/accountHub") |> ignore

startUserRoutes app
startTransferRoutes app
startAccountRoutes app

app.Run()

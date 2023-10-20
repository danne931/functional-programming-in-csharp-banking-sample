namespace Bank.Infrastructure

open Microsoft.AspNetCore.SignalR
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

open Bank.Hubs

module SignalRInfra =
   let start (builder: WebApplicationBuilder) =
      builder.Services
         .AddSignalR()
         .AddJsonProtocol(fun opts ->
            Serialization.withInjectedOptions opts.PayloadSerializerOptions)
      |> ignore

   let mapHubs (app: WebApplication) =
      app.MapHub<AccountHub>("/accountHub") |> ignore

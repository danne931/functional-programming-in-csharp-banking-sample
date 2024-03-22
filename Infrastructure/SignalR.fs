namespace Bank.Infrastructure

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

module SignalRInfra =
   let start (builder: WebApplicationBuilder) =
      builder.Services
         .AddSignalR()
         .AddHubOptions(fun opts -> opts.EnableDetailedErrors <- true)
      |> ignore

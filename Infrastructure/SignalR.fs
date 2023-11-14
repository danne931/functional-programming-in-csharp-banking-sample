namespace Bank.Infrastructure

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

module SignalRInfra =
   let start (builder: WebApplicationBuilder) =
      builder.Services
         .AddSignalR()
         .AddJsonProtocol(fun opts ->
            Serialization.withInjectedOptions opts.PayloadSerializerOptions)
      |> ignore

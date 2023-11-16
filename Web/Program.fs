open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.SignalR
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Akka.Hosting
open Akka.Cluster.Hosting
open Akka.HealthCheck.Hosting
open Akka.HealthCheck.Hosting.Web
open Akka.Event
open Akka.Logger.Serilog
open Serilog

open Bank.Infrastructure
open Bank.User.Routes
open Bank.Account.Routes
open Bank.Account.Domain
open Bank.Transfer.Routes
open Bank.Hubs
open ActorUtil

let builder = Env.builder

LogInfra.start builder

EndpointSerializationInfra.start builder

SignalRInfra.start builder

builder.Services.AddAkka(
   Env.config.AkkaSystemName,
   (fun builder provider ->
      let signalRHub =
         provider.GetRequiredService<IHubContext<AccountHub, IAccountClient>>()

      let builder =
         AkkaInfra.withClustering builder [| "web"; "signal-r-role" |]

      builder
         .WithCustomSerializer(
            "akka",
            [
               typedefof<AccountMessage>
               typedefof<AccountState>
               // NOTE: Akka ShardRegionProxy defined in Akka.Hosting below
               //       does not recognize Akkling ShardEnvelope as Akka
               //       ShardingEnvelope so need to explicitly add it for
               //       message extraction.
               //
               //       This would be unnecessary if I was using Akka.Hosting
               //       IRequiredActor<> to dependency inject the account shard
               //       region proxy into routes. However, doing so would lose
               //       Akkling's typed actor message benefits when forwarding
               //       a message from Akka ShardRegionProxy.
               typedefof<Akkling.Cluster.Sharding.ShardEnvelope>
            ],
            fun system -> AkkaSerializer(system)
         )
         .WithShardRegionProxy<ActorMetadata.AccountShardRegionMarker>(
            ActorMetadata.accountShardRegion.name,
            "account-role",
            ActorMetadata.accountShardRegion.messageExtractor
         )
         .WithDistributedPubSub("signal-r-role")
         .WithActors(fun system _ ->
            SignalRActor.start system signalRHub |> ignore)
         .ConfigureLoggers(fun builder ->
            builder.LogLevel <- LogLevel.InfoLevel
            builder.LogConfigOnStart <- false
            builder.AddLogger<SerilogLogger>() |> ignore

            builder.LogMessageFormatter <- typeof<SerilogLogMessageFormatter>)
         .WithWebHealthCheck(provider)
      |> ignore

      ())
)
|> ignore

builder.Services.AddEndpointsApiExplorer().AddSwaggerGen() |> ignore
builder.Services.WithAkkaHealthCheck(HealthCheckType.All) |> ignore

let app = builder.Build()

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore
app.UseSerilogRequestLogging() |> ignore

// Available at endpoint: /swagger/index.html
if app.Environment.IsDevelopment() then
   app.UseSwagger().UseSwaggerUI() |> ignore

app.MapHub<AccountHub>("/accountHub") |> ignore

// Available at endpoint: /healthz/akka
app.MapAkkaHealthCheckRoutes(
   optionConfigure =
      fun _ opt ->
         opt.ResponseWriter <-
            fun httpCtx healthReport ->
               Helper.JsonResponseWriter(httpCtx, healthReport)
)
|> ignore

startUserRoutes app
startTransferRoutes app
startAccountRoutes app

app.Run()

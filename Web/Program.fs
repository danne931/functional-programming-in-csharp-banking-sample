open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Serilog
open Akka.Hosting
open Akka.HealthCheck.Hosting
open Akka.HealthCheck.Hosting.Web
open Akka.Event
open Akka.Logger.Serilog
open Petabridge.Cmd.Host
open Petabridge.Cmd.Cluster
open Petabridge.Cmd.Cluster.Sharding

open Bank.Infrastructure
open Bank.User.Routes
open Bank.Account.Routes
open Bank.Transfer.Routes

let builder = Env.builder

LogInfra.start builder

EndpointSerializationInfra.start builder

SignalRInfra.start builder

QuartzInfra.start builder

AkkaInfra.start builder
<| fun (builder: AkkaConfigurationBuilder) (provider: IServiceProvider) ->
   builder
      .ConfigureLoggers(fun builder ->
         builder.LogLevel <- LogLevel.InfoLevel
         builder.LogConfigOnStart <- false
         builder.AddLogger<SerilogLogger>() |> ignore

         builder.LogMessageFormatter <- typeof<SerilogLogMessageFormatter>)
      .AddPetabridgeCmd(fun cmd ->
         cmd.RegisterCommandPalette(ClusterCommands.Instance) |> ignore

         cmd.RegisterCommandPalette(ClusterShardingCommands.Instance) |> ignore)
      .WithWebHealthCheck(provider)
#if DEBUG
      .AddStartup(StartupTask(fun sys _ -> PostgresSeeder.seed sys))
#endif
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

SignalRInfra.mapHubs app

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

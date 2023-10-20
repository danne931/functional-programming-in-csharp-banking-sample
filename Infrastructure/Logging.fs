namespace Bank.Infrastructure

open Microsoft.AspNetCore.Builder
open Serilog
open Serilog.Sinks.SystemConsole
open Serilog.Formatting.Compact

module LogInfra =
   let start (builder: WebApplicationBuilder) =
      // NOTE: Initial logger logs errors during during start up.
      //       It's replaced by the config in UseSerilog below.
      Log.Logger <- LoggerConfiguration().WriteTo.Console().CreateLogger()

      builder.Host.UseSerilog(fun ctx services loggerConfig ->
         loggerConfig.ReadFrom
            .Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(theme = Themes.AnsiConsoleTheme.Code)
            .WriteTo.File(CompactJsonFormatter(), Env.config.SerilogOutputFile)
         |> ignore)
      |> ignore

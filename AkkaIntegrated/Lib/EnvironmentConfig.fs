[<RequireQualifiedAccess>]
module EnvironmentConfig

open Microsoft.AspNetCore.Builder
open FsConfig

let builder = WebApplication.CreateBuilder()

type Connection = {
   Postgres: string
   PostgresAdoFormat: string
}

type AkkaPersistence = {
   DbProvider: string
   JournalTableName: string
   SnapshotTableName: string
}

type Quartz = {
   SchedulerName: string
   TablePrefix: string
}

// NOTE:
// For local development, EmailBearerToken & SupportEmail are set in ~/.microsoft/usersecrets.
// See https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-7.0&tabs=linux#set-a-secret

type BankConfig = {
   ConnectionStrings: Connection
   AkkaPersistence: AkkaPersistence
   AkkaSystemName: string
   AkkaRemoteHost: string
   AkkaRemotePort: int
   AkkaClusterSeedNodes: string list
   Quartz: Quartz
   SerilogOutputFile: string
   EmailServiceUri: string
   EmailBearerToken: string option
   SupportEmail: string option
}

let private errorMessage missing =
   $"""
   ========================================================

     Missing {missing} configuration setting(s)

   ========================================================
   """

let config =
   match AppConfig(builder.Configuration).Get<BankConfig>() with
   | Ok o ->
      let missing =
         [
            "EmailBearerToken", Option.isNone o.EmailBearerToken
            "SupportEmail", Option.isNone o.SupportEmail
         ]
         |> List.fold
            (fun acc (key, isNone) -> if isNone then key :: acc else acc)
            []

      if not missing.IsEmpty then
         let errMsg = errorMessage missing
#if !DEBUG
         // Fail if running app outside local dev environment.
         failwith errMsg
#else
         printfn "%A" errMsg
         o
#endif
      else
         o
   | Error err ->
      match err with
      | NotFound key -> invalidArg key "Not found"
      | BadValue(key, value) -> invalidArg key $"{value} is invalid type for"
      | NotSupported key -> invalidArg key "Not supported"

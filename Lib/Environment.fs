[<RequireQualifiedAccess>]
module Env

open Microsoft.AspNetCore.Builder
open System.Net
open FsConfig

let builder = WebApplication.CreateBuilder()

let isDev = builder.Environment.EnvironmentName = "Development"

type Connection = {
   Postgres: string
   PostgresAdoFormat: string
}

type Quartz = {
   SchedulerName: string
   TablePrefix: string
}

type AkkaPersistence = {
   DbProvider: string
   JournalTableName: string
   SnapshotTableName: string
}

type ClusterDiscoveryStartup = { EndpointNames: string list }

type ClusterSeedNodeStartup = { SeedNodes: string list }

type AkkaRemoting = { Host: string; Port: int }

type PetabridgeCmdRemoting = { Port: int }

type MockThirdPartyBank = { Host: IPAddress; Port: int }

type ClusterStartupMethod =
   | SeedNode of ClusterSeedNodeStartup
   | DiscoveryConfig of ClusterDiscoveryStartup

// NOTE:
// For local development, EmailBearerToken & SupportEmail are set in ~/.microsoft/usersecrets.
// See https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-7.0&tabs=linux#set-a-secret

type private BankConfigInput = {
   ConnectionStrings: Connection
   AkkaPersistence: AkkaPersistence
   AkkaSystemName: string
   AkkaRemoting: {| Host: string option; Port: int |}
   PetabridgeCmdRemoting: PetabridgeCmdRemoting
   ClusterStartupMethod: string
   ClusterDiscoveryStartup: ClusterDiscoveryStartup option
   ClusterSeedNodeStartup: ClusterSeedNodeStartup option
   Quartz: Quartz
   SerilogOutputFile: string
   EmailServiceUri: string
   EmailBearerToken: string option
   SupportEmail: string option
   MockThirdPartyBank: {| Host: string option; Port: int |}
}

type BankConfig = {
   ConnectionStrings: Connection
   AkkaPersistence: AkkaPersistence
   AkkaSystemName: string
   AkkaRemoting: AkkaRemoting
   PetabridgeCmdRemoting: PetabridgeCmdRemoting
   ClusterStartupMethod: ClusterStartupMethod
   Quartz: Quartz
   SerilogOutputFile: string
   EmailServiceUri: string
   EmailBearerToken: string option
   SupportEmail: string option
   MockThirdPartyBank: MockThirdPartyBank
}

let private errorMessage missing =
   $"""
   ========================================================

     Missing {missing} configuration setting(s)

   ========================================================
   """

let private getMockThirdPartyBankHost (host: string option) =
   match host with
   | Some ip -> IPAddress.Parse ip
   | None ->
      try
         // Referencing container by name to resolve IP for
         // mock third party bank server.
         Dns.GetHostAddresses("mock-third-party-bank")[0]
      with _ ->
         if not isDev then
            failwith
               """
               IP for mock third party bank doesn't exist.
               Misconfigured container name.
               """
         else
            printfn "Configuring localhost for domestic transfer."
            IPAddress.Loopback

let config =
   match AppConfig(builder.Configuration).Get<BankConfigInput>() with
   | Ok input ->
      let missing =
         [
            "EmailBearerToken", Option.isNone input.EmailBearerToken
            "SupportEmail", Option.isNone input.SupportEmail
         ]
         |> List.fold
            (fun acc (key, isNone) -> if isNone then key :: acc else acc)
            []

      if not missing.IsEmpty then
         let errMsg = errorMessage missing

         if not isDev then
            // Fail if running app outside local dev environment.
            failwith errMsg
         else
            printfn "%A" errMsg

      let clusterStartupMethod =
         match input.ClusterStartupMethod with
         | "Discovery" ->
            ClusterStartupMethod.DiscoveryConfig
               input.ClusterDiscoveryStartup.Value
         | "SeedNode" ->
            ClusterStartupMethod.SeedNode input.ClusterSeedNodeStartup.Value
         | _ -> failwith "Unknown cluster startup method"

      {
         ConnectionStrings = input.ConnectionStrings
         AkkaPersistence = input.AkkaPersistence
         AkkaSystemName = input.AkkaSystemName
         ClusterStartupMethod = clusterStartupMethod
         AkkaRemoting = {
            Host =
               input.AkkaRemoting.Host
               |> Option.defaultValue (Dns.GetHostName().ToLower())
            Port = input.AkkaRemoting.Port
         }
         PetabridgeCmdRemoting = input.PetabridgeCmdRemoting
         Quartz = input.Quartz
         SerilogOutputFile = input.SerilogOutputFile
         EmailServiceUri = input.EmailServiceUri
         EmailBearerToken = input.EmailBearerToken
         SupportEmail = input.SupportEmail
         MockThirdPartyBank = {
            Host = getMockThirdPartyBankHost input.MockThirdPartyBank.Host
            Port = input.MockThirdPartyBank.Port
         }
      }
   | Error err ->
      match err with
      | NotFound key -> invalidArg key "Not found"
      | BadValue(key, value) -> invalidArg key $"{value} is invalid type for"
      | NotSupported key -> invalidArg key "Not supported"

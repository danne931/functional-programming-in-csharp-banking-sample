[<RequireQualifiedAccess>]
module Env

open Microsoft.AspNetCore.Builder
open System
open System.Net
open FsConfig
open Lib.Types

let builder = WebApplication.CreateBuilder()

let isDev = builder.Environment.EnvironmentName = "Development"

type Connection = {
   Postgres: string
   PostgresAdoFormat: string
}

type AkkaPersistence = {
   DbProvider: string
   JournalTableName: string
   SnapshotTableName: string
}

type ClusterDiscoveryStartup = {
   EndpointNames: string list
   Port: int
}

type ClusterDiscoveryKubernetesStartup = {
   PodLabelSelector: string
   PortName: string
   RequiredContactPointsNr: int
}

type AkkaHealthCheck = {
   ReadinessPort: int
   LivenessPort: int
}

type ClusterSeedNodeStartup = { SeedNodes: string list }

type AkkaRemoting = { Host: string; Port: int }

type PetabridgeCmdRemoting = { Port: int }

type ClusterStartupMethod =
   | SeedNode of ClusterSeedNodeStartup
   | DiscoveryConfig of ClusterDiscoveryStartup
   | DiscoveryKubernetes of ClusterDiscoveryKubernetesStartup

type StreamThrottleInput = {|
   Count: int option
   Burst: int option
   Seconds: int option
|}

type StreamChunkingInput = {|
   Size: int option
   Seconds: int option
|}

type StreamBackoffRestartSettingsInput = {|
   MinBackoffSeconds: int option
   MaxBackoffSeconds: int option
   RandomFactor: float option
   MaxRestarts: int option
   MaxRestartsWithinSeconds: int option
|}

let streamBackoffRestartSettingsFromInput
   (input: StreamBackoffRestartSettingsInput)
   =
   let restartSettings =
      Akka.Streams.RestartSettings.Create(
         input.MinBackoffSeconds
         |> Option.defaultValue 3
         |> TimeSpan.FromSeconds,

         input.MaxBackoffSeconds
         |> Option.defaultValue 30
         |> TimeSpan.FromSeconds,

         // Adds 20% "noise" to vary intervals slightly
         input.RandomFactor |> Option.defaultValue 0.2
      )

   restartSettings.WithMaxRestarts(
      input.MaxRestarts |> Option.defaultValue 10,

      input.MaxRestartsWithinSeconds
      |> Option.defaultValue 20
      |> TimeSpan.FromSeconds
   )

type private BankConfigInput = {
   ConnectionStrings: Connection
   AkkaRemoting: {| Host: string option; Port: int |}
   PetabridgeCmdRemoting: PetabridgeCmdRemoting
   ClusterStartupMethod: string
   ClusterDiscoveryStartup: ClusterDiscoveryStartup option
   ClusterDiscoveryKubernetesStartup: ClusterDiscoveryKubernetesStartup option
   ClusterSeedNodeStartup: ClusterSeedNodeStartup option
   AkkaHealthCheck: {|
      ReadinessPort: int option
      LivenessPort: int option
   |}
   BillingCycleFanoutThrottle: StreamThrottleInput
   AccountDeleteThrottle: StreamThrottleInput
   AccountEventProjectionChunking: StreamChunkingInput
   BillingStatementPersistenceChunking: StreamChunkingInput
   BillingStatementPersistenceBackoffRestart: StreamBackoffRestartSettingsInput
}

type BankConfig = {
   ConnectionStrings: Connection
   AkkaPersistence: AkkaPersistence
   AkkaSystemName: string
   AkkaRemoting: AkkaRemoting
   PetabridgeCmdRemoting: PetabridgeCmdRemoting
   ClusterStartupMethod: ClusterStartupMethod
   SerilogOutputFile: string
   AkkaHealthCheck: AkkaHealthCheck
   BillingCycleFanoutThrottle: StreamThrottle
   AccountDeleteThrottle: StreamThrottle
   AccountEventProjectionChunking: StreamChunking
   BillingStatementPersistenceChunking: StreamChunking
   BillingStatementPersistenceBackoffRestart: Akka.Streams.RestartSettings
}

let config =
   match AppConfig(builder.Configuration).Get<BankConfigInput>() with
   | Ok input ->
      let clusterStartupMethod =
         match input.ClusterStartupMethod with
         | "Discovery" ->
            ClusterStartupMethod.DiscoveryConfig
               input.ClusterDiscoveryStartup.Value
         | "DiscoveryKubernetes" ->
            ClusterStartupMethod.DiscoveryKubernetes
               input.ClusterDiscoveryKubernetesStartup.Value
         | "SeedNode" ->
            ClusterStartupMethod.SeedNode input.ClusterSeedNodeStartup.Value
         | _ -> failwith "Unknown cluster startup method"

      {
         ConnectionStrings = input.ConnectionStrings
         AkkaPersistence = {
            DbProvider = "PostgreSQL.16"
            JournalTableName = "akka_event_journal"
            SnapshotTableName = "akka_snapshots"
         }
         AkkaSystemName = "bank"
         ClusterStartupMethod = clusterStartupMethod
         AkkaRemoting = {
            Host =
               input.AkkaRemoting.Host
               |> Option.defaultValue (Dns.GetHostName().ToLower())
            Port = input.AkkaRemoting.Port
         }
         PetabridgeCmdRemoting = input.PetabridgeCmdRemoting
         SerilogOutputFile = "logs.json"
         AkkaHealthCheck = {
            LivenessPort =
               input.AkkaHealthCheck.LivenessPort |> Option.defaultValue 11000
            ReadinessPort =
               input.AkkaHealthCheck.ReadinessPort |> Option.defaultValue 11001
         }
         BillingCycleFanoutThrottle = {
            Count =
               input.BillingCycleFanoutThrottle.Count
               |> Option.defaultValue 10000
            Burst =
               input.BillingCycleFanoutThrottle.Burst
               |> Option.defaultValue 10000
            Duration =
               input.BillingCycleFanoutThrottle.Seconds
               |> Option.defaultValue 1
               |> TimeSpan.FromSeconds
         }
         AccountDeleteThrottle = {
            Count = input.AccountDeleteThrottle.Count |> Option.defaultValue 5
            Burst = input.AccountDeleteThrottle.Burst |> Option.defaultValue 5
            Duration =
               input.AccountDeleteThrottle.Seconds
               |> Option.defaultValue 1
               |> TimeSpan.FromSeconds
         }
         AccountEventProjectionChunking = {
            Size =
               input.AccountEventProjectionChunking.Size
               |> Option.defaultValue 100
            Duration =
               input.AccountEventProjectionChunking.Seconds
               |> Option.defaultValue 15
               |> TimeSpan.FromSeconds
         }
         BillingStatementPersistenceChunking = {
            Size =
               input.BillingStatementPersistenceChunking.Size
               |> Option.defaultValue 1000
            Duration =
               input.BillingStatementPersistenceChunking.Seconds
               |> Option.defaultValue 3
               |> TimeSpan.FromSeconds
         }
         BillingStatementPersistenceBackoffRestart =
            streamBackoffRestartSettingsFromInput
               input.BillingStatementPersistenceBackoffRestart
      }
   | Error err ->
      match err with
      | NotFound key -> invalidArg key "Not found"
      | BadValue(key, value) -> invalidArg key $"{value} is invalid type for"
      | NotSupported key -> invalidArg key "Not supported"

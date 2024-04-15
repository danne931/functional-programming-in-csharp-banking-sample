[<RequireQualifiedAccess>]
module Env

open Microsoft.AspNetCore.Builder
open System
open System.Net
open System.IO
open FsConfig
open Lib.Types

// Serve static files out of the UI/dist directory during development.
// This dist directory is copied over to the default Web/wwwroot
// location during builds.  See BuildUI task in build.fsx.
let builder =
#if DEBUG
   WebApplication.CreateBuilder(
      WebApplicationOptions(
         WebRootPath =
            Path.Combine(Environment.CurrentDirectory, "..", "UI/dist")
      )
   )
#else
   WebApplication.CreateBuilder()
#endif

let isDev = builder.Environment.EnvironmentName = "Development"
let isStaging = builder.Environment.EnvironmentName = "Staging"
let isProd = builder.Environment.EnvironmentName = "Production"
let allowLiveLoadTest = not isProd

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

// Recommended to have ~10 shards per node. If 4 account nodes are deployed in
// K8s (see Deploy/K8s/account.ts account cluster config) then this value
// should be ~40.
//
// Changing this value requires a cluster restart.
type AccountCluster = { NumberOfShards: int }

type AkkaRemoting = { Host: string; Port: int }

type PetabridgeCmdRemoting = { Port: int }

type ClusterStartupMethod =
   | SeedNode of ClusterSeedNodeStartup
   | DiscoveryConfig of ClusterDiscoveryStartup
   | DiscoveryKubernetes of ClusterDiscoveryKubernetesStartup

type BackoffSupervisorInput = {
   MinBackoffSeconds: float option
   MaxBackoffSeconds: float option
   RandomFactor: float option
   MaxNrOfRetries: int option
   ResetCounterAfterSeconds: int option
}

let backoffSupervisorOptionsFromInput (input: BackoffSupervisorInput) = {
   MinBackoff =
      input.MinBackoffSeconds |> Option.defaultValue 3 |> TimeSpan.FromSeconds
   MaxBackoff =
      input.MaxBackoffSeconds |> Option.defaultValue 30 |> TimeSpan.FromSeconds
   // Adds 20% "noise" to vary intervals slightly
   RandomFactor = input.RandomFactor |> Option.defaultValue 0.2
   MaxNrOfRetries = input.MaxNrOfRetries |> Option.defaultValue -1
   ResetCounterAfter =
      input.ResetCounterAfterSeconds
      |> Option.defaultValue 60
      |> TimeSpan.FromSeconds
}

type PersistenceSupervisorInput = {
   MinBackoffSeconds: float option
   MaxBackoffSeconds: float option
   RandomFactor: float option
   MaxNrOfRetries: int option
}

let persistentSupervisorOptionsFromInput (input: PersistenceSupervisorInput) = {
   MinBackoff =
      input.MinBackoffSeconds |> Option.defaultValue 0.1 |> TimeSpan.FromSeconds
   MaxBackoff =
      input.MaxBackoffSeconds |> Option.defaultValue 2 |> TimeSpan.FromSeconds
   // Adds 20% "noise" to vary intervals slightly
   RandomFactor = input.RandomFactor |> Option.defaultValue 0.2
   MaxNrOfRetries = input.MaxNrOfRetries |> Option.defaultValue 10
}

type StreamThrottleInput = {|
   Count: int option
   Burst: int option
   Seconds: float option
|}

type StreamChunkingInput = {|
   Size: int option
   Seconds: float option
|}

type StreamBackoffRestartSettingsInput = {|
   MinBackoffSeconds: float option
   MaxBackoffSeconds: float option
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
   AccountCluster: {| NumberOfShards: int option |}
   AccountActorSupervisor: PersistenceSupervisorInput
   AccountDeleteThrottle: StreamThrottleInput
   AccountEventProjectionChunking: StreamChunkingInput
   AccountEventReadModelPersistenceBackoffRestart:
      StreamBackoffRestartSettingsInput
   BillingStatementPersistenceChunking: StreamChunkingInput
   BillingStatementPersistenceBackoffRestart: StreamBackoffRestartSettingsInput
   CircuitBreakerActorSupervisor: BackoffSupervisorInput
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
   AccountCluster: AccountCluster
   AccountActorSupervisor: PersistenceSupervisorOptions
   AccountDeleteThrottle: StreamThrottle
   AccountEventProjectionChunking: StreamChunking
   AccountEventReadModelPersistenceBackoffRestart: Akka.Streams.RestartSettings
   AccountEventReadModelRetryPersistenceAfter: TimeSpan
   BillingStatementPersistenceChunking: StreamChunking
   BillingStatementPersistenceBackoffRestart: Akka.Streams.RestartSettings
   BillingStatementRetryPersistenceAfter: TimeSpan
   CircuitBreakerActorSupervisor: BackoffSupervisorOptions
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
               |> Option.defaultValue 5000
            Burst =
               input.BillingCycleFanoutThrottle.Burst
               |> Option.defaultValue 5000
            Duration =
               input.BillingCycleFanoutThrottle.Seconds
               |> Option.defaultValue 1
               |> TimeSpan.FromSeconds
         }
         AccountCluster = {
            NumberOfShards =
               input.AccountCluster.NumberOfShards |> Option.defaultValue 10
         }
         AccountActorSupervisor =
            persistentSupervisorOptionsFromInput input.AccountActorSupervisor
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
               |> Option.defaultValue 5000
            Duration =
               input.AccountEventProjectionChunking.Seconds
               |> Option.defaultValue 5
               |> TimeSpan.FromSeconds
         }
         AccountEventReadModelPersistenceBackoffRestart =
            streamBackoffRestartSettingsFromInput
               input.AccountEventReadModelPersistenceBackoffRestart
         AccountEventReadModelRetryPersistenceAfter = TimeSpan.FromSeconds 7
         BillingStatementPersistenceChunking = {
            Size =
               input.BillingStatementPersistenceChunking.Size
               |> Option.defaultValue 5000
            Duration =
               input.BillingStatementPersistenceChunking.Seconds
               |> Option.defaultValue 1
               |> TimeSpan.FromSeconds
         }
         BillingStatementPersistenceBackoffRestart =
            streamBackoffRestartSettingsFromInput
               input.BillingStatementPersistenceBackoffRestart
         BillingStatementRetryPersistenceAfter = TimeSpan.FromSeconds 7
         CircuitBreakerActorSupervisor =
            backoffSupervisorOptionsFromInput
               input.CircuitBreakerActorSupervisor
      }
   | Error err ->
      match err with
      | NotFound key -> invalidArg key "Not found"
      | BadValue(key, value) -> invalidArg key $"{value} is invalid type for"
      | NotSupported key -> invalidArg key "Not supported"

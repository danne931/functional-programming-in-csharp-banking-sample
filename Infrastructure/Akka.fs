namespace Bank.Infrastructure

open System
open Akka.Hosting
open Akka.Remote.Hosting
open Akka.Cluster.Hosting
open Akka.Cluster.Hosting.SBR
open Akka.HealthCheck.Hosting
open Akka.Persistence.Sql.Hosting
open Akka.Management
open Akka.Management.Cluster.Bootstrap
open Akka.Discovery.Config.Hosting
open Akka.Discovery.KubernetesApi
open Akka.Event
open Akka.Logger.Serilog
open Petabridge.Cmd.Host
open Petabridge.Cmd.Cluster
open Petabridge.Cmd.Cluster.Sharding

module AkkaInfra =
   let getJournalOpts () =
      let journalOpts = SqlJournalOptions()

      journalOpts.ConnectionString <-
         Env.config.ConnectionStrings.PostgresAdoFormat

      journalOpts.ProviderName <- Env.config.AkkaPersistence.DbProvider
      journalOpts.AutoInitialize <- true
      let jdo = JournalDatabaseOptions(DatabaseMapping.Default)
      let jto = JournalTableOptions()
      jto.TableName <- Env.config.AkkaPersistence.JournalTableName
      jto.UseWriterUuidColumn <- true
      jdo.JournalTable <- jto
      journalOpts.DatabaseOptions <- jdo
      journalOpts

   let getSnapshotOpts () =
      let snapshotOpts = SqlSnapshotOptions()

      snapshotOpts.ConnectionString <-
         Env.config.ConnectionStrings.PostgresAdoFormat

      snapshotOpts.ProviderName <- Env.config.AkkaPersistence.DbProvider
      snapshotOpts.AutoInitialize <- true
      let sdo = SnapshotDatabaseOptions(DatabaseMapping.Default)
      let sto = SnapshotTableOptions()
      sto.TableName <- Env.config.AkkaPersistence.SnapshotTableName
      sdo.SnapshotTable <- sto
      snapshotOpts.DatabaseOptions <- sdo
      snapshotOpts

   let withClustering
      (roles: string array)
      (builder: AkkaConfigurationBuilder)
      =
      let remote = Env.config.AkkaRemoting

      // TODO: Investigate cost/benefit of using Kubernetes lease option for
      // SplitBrainResolver, Cluster Singleton, & Cluster Sharding
      // https://github.com/akkadotnet/Akka.Management/tree/dev/src/coordination/kubernetes/Akka.Coordination.KubernetesApi
      // https://getakka.net/articles/clustering/split-brain-resolver.html#lease-majority
      let clusterOpts =
         ClusterOptions(
            Roles = roles,
            SplitBrainResolver = KeepMajorityOption()
         )

      match Env.config.ClusterStartupMethod with
      | Env.DiscoveryConfig conf ->
         let serviceName = "akka-management"

         builder
            .WithAkkaManagement(fun (setup: AkkaManagementSetup) ->
               setup.Http.HostName <- remote.Host
               setup.Http.Port <- conf.Port
               setup.Http.BindHostName <- "0.0.0.0"
               setup.Http.BindPort <- conf.Port)
            .WithClusterBootstrap(fun opts ->
               opts.ContactPointDiscovery.ServiceName <- serviceName
               opts.ContactPointDiscovery.RequiredContactPointsNr <- 2)
            .WithConfigDiscovery(fun opts ->
               let service = Service()
               service.Name <- serviceName

               service.Endpoints <-
                  conf.EndpointNames
                  |> List.map (fun hostName -> $"{hostName}:{conf.Port}")
                  |> List.toArray

               let li = System.Collections.Generic.List<Service>()
               li.Add(service)
               opts.Services <- li)
            .WithRemoting(remote.Host, remote.Port)
            .WithClustering(clusterOpts)
      | Env.DiscoveryKubernetes conf ->
         builder
            .WithClusterBootstrap(
               (fun opts ->
                  // NOTE:
                  // Setting NewClusterEnabled to false after an initial cluster has formed is recommended
                  // to prevent new clusters forming during a network partition when nodes are redeployed or restarted.
                  // opts.NewClusterEnabled <- false

                  opts.ContactPointDiscovery.PortName <- conf.PortName

                  opts.ContactPointDiscovery.ContactWithAllContactPoints <-
                     true

                  opts.ContactPointDiscovery.RequiredContactPointsNr <-
                     conf.RequiredContactPointsNr

                  opts.ContactPointDiscovery.StableMargin <-
                     TimeSpan.FromSeconds 5),
               autoStart = true
            )
            .WithKubernetesDiscovery(conf.PodLabelSelector)
            .WithRemoting(fun o ->
               o.HostName <- "0.0.0.0"
               o.PublicHostName <- remote.Host
               o.Port <- remote.Port)
            .WithClustering(clusterOpts)
      | Env.SeedNode conf ->
         // Manual seed node approach for local dev without Docker
         clusterOpts.SeedNodes <- List.toArray conf.SeedNodes

         builder
            .WithRemoting(remote.Host, remote.Port)
            .WithClustering(clusterOpts)

   let withPetabridgeCmd (builder: AkkaConfigurationBuilder) =
      let hocon =
         sprintf
            """
            petabridge.cmd: {
               host: "0.0.0.0"
               port: %i
            }
            """
            Env.config.PetabridgeCmdRemoting.Port

      builder
         .AddHocon(hocon, HoconAddMode.Prepend)
         .AddPetabridgeCmd(fun cmd ->
            cmd.RegisterCommandPalette(ClusterCommands.Instance) |> ignore

            cmd.RegisterCommandPalette(ClusterShardingCommands.Instance)
            |> ignore)

   let withHealthCheck (builder: AkkaConfigurationBuilder) =
      builder.WithHealthCheck(fun opts ->
         opts.AddProviders(HealthCheckType.All) |> ignore
         opts.Liveness.Transport <- HealthCheckTransport.Tcp
         opts.Liveness.TcpPort <- Env.config.AkkaHealthCheck.LivenessPort
         opts.Readiness.Transport <- HealthCheckTransport.Tcp
         opts.Readiness.TcpPort <- Env.config.AkkaHealthCheck.ReadinessPort)

   let withLogging (builder: AkkaConfigurationBuilder) =
      builder.ConfigureLoggers(fun builder ->
         builder.LogLevel <- LogLevel.InfoLevel
         builder.LogConfigOnStart <- true
         builder.AddLogger<SerilogLogger>() |> ignore

         builder.WithDefaultLogMessageFormatter<SerilogLogMessageFormatter>()
         |> ignore)

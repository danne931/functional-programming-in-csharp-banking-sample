namespace Bank.Infrastructure

open Akka.Hosting
open Akka.Remote.Hosting
open Akka.Cluster.Hosting
open Akka.Cluster.Hosting.SBR
open Akka.Persistence.Sql.Hosting
open Akka.Management
open Akka.Management.Cluster.Bootstrap
open Akka.Discovery.Config.Hosting

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
      (builder: AkkaConfigurationBuilder)
      (roles: string array)
      =
      let remote = Env.config.AkkaRemoting

      let clusterOpts =
         ClusterOptions(
            Roles = roles,
            SplitBrainResolver = KeepMajorityOption()
         )

      match Env.config.ClusterStartupMethod with
      | Env.DiscoveryConfig conf ->
         let managementPort = 8558
         let serviceName = "akka-management"

         builder
            .WithAkkaManagement(fun (setup: AkkaManagementSetup) ->
               setup.Http.HostName <- remote.Host
               setup.Http.Port <- managementPort
               setup.Http.BindHostName <- "0.0.0.0"
               setup.Http.BindPort <- managementPort)
            .WithClusterBootstrap(fun opts ->
               opts.ContactPointDiscovery.ServiceName <- serviceName
               opts.ContactPointDiscovery.RequiredContactPointsNr <- 2)
            .WithConfigDiscovery(fun opts ->
               let service = Service()
               service.Name <- serviceName

               service.Endpoints <-
                  conf.EndpointNames
                  |> List.map (fun hostName -> $"{hostName}:{managementPort}")
                  |> List.toArray

               let li = System.Collections.Generic.List<Service>()
               li.Add(service)
               opts.Services <- li)
            .WithRemoting(remote.Host, remote.Port)
            .WithClustering(clusterOpts)
      | Env.SeedNode conf ->
         // Manual seed node approach for local dev without Docker
         clusterOpts.SeedNodes <- List.toArray conf.SeedNodes

         builder
            .WithRemoting(remote.Host, remote.Port)
            .WithClustering(clusterOpts)

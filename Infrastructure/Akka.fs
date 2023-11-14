namespace Bank.Infrastructure

open Akka.Hosting
open Akka.Remote.Hosting
open Akka.Cluster.Hosting
open Akka.Persistence.Sql.Hosting

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
      (port: int option)
      =
      let port = Option.defaultValue 0 port

      builder
         .WithRemoting(Env.config.AkkaRemoteHost, port)
         .WithClustering(
            ClusterOptions(
               SeedNodes = List.toArray Env.config.AkkaClusterSeedNodes,
               Roles = roles
            )
         )

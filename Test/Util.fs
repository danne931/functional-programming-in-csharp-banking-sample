module Util

open System
open Expecto
open Akkling
open Akkling.Cluster.Sharding

let akkaTest
   (testDescription: string)
   (config: Akka.Configuration.Config option)
   (testBody: TestKit.Tck -> unit)
   =
   let akkaTester =
      match config with
      | Some config -> TestKit.test config
      | None -> TestKit.testDefault

   testCase testDescription <| fun () -> akkaTester <| testBody

let getAccountEntityRef (actorRef: IActorRef<_>) (entityId: Guid) =
   let shardRegionName = "mock-account-shard"

   let fac = {
      TypeName = shardRegionName
      ShardRegion = untyped actorRef
   }

   fac.RefFor shardRegionName <| string entityId

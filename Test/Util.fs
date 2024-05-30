module Util

open System
open Expecto
open Akkling
open Akkling.Cluster.Sharding
open Lib.SharedTypes

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

let getAccountEntityRef (actorRef: IActorRef<_>) (accountId: AccountId) =
   let shardRegionName = "mock-account-shard"

   let fac = {
      TypeName = shardRegionName
      ShardRegion = untyped actorRef
   }

   let (AccountId id) = accountId
   fac.RefFor shardRegionName <| string id

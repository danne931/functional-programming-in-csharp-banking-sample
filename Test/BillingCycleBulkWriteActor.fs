module BillingCycleBulkWriteActorTests

open System.Threading
open Expecto
open Akkling
open FsToolkit.ErrorHandling

module ATK = Akkling.TestKit
module TActor = BillingCycleBulkWriteActor

open BillingStatement
open Util

let config () =
   Configuration.parse
      """
      billing-cycle-bulk-write-mailbox: {
         mailbox-type: "BillingCycleBulkWriteActor+PriorityMailbox, BillingCycle.App"
      }
      """

let billingPersistence: BillingPersistence = {
   saveBillingStatements = fun statements -> TaskResult.ok [ statements.Length ]
}

let stateAfter2Registrations: TActor.BulkWriteState = {
   IsScheduled = true
   Billing = [ Stub.billingStatement; Stub.billingStatement ]
}

[<Tests>]
let tests =
   testList "Billing Cycle Bulk Write Actor" [
      akkaTest
         "RegisterBillingStatement message should accumulate billing statements in actor state"
      <| Some(config ())
      <| fun tck ->
         let ref = TActor.start tck.Sys billingPersistence
         let msg = TActor.RegisterBillingStatement Stub.billingStatement
         ref <! msg
         ref <! msg
         ref <! TActor.Lookup
         ATK.expectMsg tck stateAfter2Registrations |> ignore

      akkaTest "Actor state is reset after PersistBillingStatements message"
      <| Some(config ())
      <| fun tck ->
         let ref = TActor.start tck.Sys billingPersistence
         let msg = TActor.RegisterBillingStatement Stub.billingStatement
         ref <! msg
         ref <! msg
         Thread.Sleep(100)
         ref <! TActor.PersistBillingStatements
         ref <! TActor.Lookup
         ATK.expectMsg tck TActor.initState |> ignore

      akkaTest "Actor state is reset after batch size reached"
      <| Some(config ())
      <| fun tck ->
         let ref = TActor.start tck.Sys billingPersistence
         let msg = TActor.RegisterBillingStatement Stub.billingStatement
         let maxMinus1 = TActor.batchSizeLimit - 1

         for _ in [ 1..maxMinus1 ] do
            ref <! msg

         ref <! TActor.Lookup
         let res = tck.ExpectMsg<TActor.BulkWriteState>()

         Expect.equal
            res.Billing.Length
            maxMinus1
            $"actor should have {maxMinus1} billing statements in state"

         // This message increases the batch size to the limit.
         // Billing statements are persisted & state is restored
         // to its initial state.
         ref <! msg

         ref <! TActor.Lookup
         ATK.expectMsg tck TActor.initState |> ignore
   ]

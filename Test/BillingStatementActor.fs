module BillingStatementActorTests

open System
open Expecto
open Akkling
open FsToolkit.ErrorHandling

module ATK = Akkling.TestKit

open BillingStatement
open Util
open Lib.Types
open Lib.BulkWriteStreamFlow

// Change default test scheduler so BulkWriteStreamFlow used in
// BillingStatementActor can schedule a message to tell itself
// to retry persisting failed writes of BillingStatements.
let config =
   Configuration.parse
      """
      akka.scheduler.implementation = "Akka.Actor.HashedWheelTimerScheduler, Akka"
      """

let billingPersistenceFail: BillingPersistence = {
   saveBillingStatements =
      fun _ -> Exception("test-fail") |> Err.DatabaseError |> TaskResult.error
}

let billingPersistenceFailOnce () : BillingPersistence =
   let mutable counter = 0

   {
      saveBillingStatements =
         fun statements ->
            if counter = 0 then
               counter <- 1
               Exception("test-fail") |> Err.DatabaseError |> TaskResult.error
            else
               TaskResult.ok [ statements.Length ]
   }

let stateAfter2Registrations = {
   IsRetryScheduled = true
   FailedWrites = [ Stub.billingStatement; Stub.billingStatement ]
}

let retryPersistenceAfter = TimeSpan.FromSeconds 1

let init (tck: TestKit.Tck) (chunkSize: int) (persistence: BillingPersistence) =
   let props =
      BillingStatementActor.actorProps tck.Sys persistence {
         Size = chunkSize
         Duration = TimeSpan.FromSeconds 1
      }
      <| Stub.akkaStreamsRestartSettings ()
      <| retryPersistenceAfter

   spawn tck "mock-billing" props

[<Tests>]
let tests =
   testList "Billing Statement Actor" [
      akkaTest
         "RegisterBillingStatement message should accumulate billing statements in actor state"
      <| Some config
      <| fun tck ->
         let ref = init tck 2 billingPersistenceFail

         let msg =
            BillingStatementMessage.RegisterBillingStatement
               Stub.billingStatement

         ref <! msg
         ref <! msg
         ref <! BillingStatementMessage.GetFailedWrites

         let (failedWrites: BillingStatement seq) = ATK.receiveOne tck

         Expect.hasLength
            failedWrites
            2
            "should contain 2 billing statements which failed to persist"

      akkaTest "Failed billing statement persist requests should be retried"
      <| Some config
      <| fun tck ->
         let ref = init tck 1 <| billingPersistenceFailOnce ()

         let msg =
            BillingStatementMessage.RegisterBillingStatement
               Stub.billingStatement

         ref <! msg
         ref <! BillingStatementMessage.GetFailedWrites

         let (failedWrites: BillingStatement seq) = ATK.receiveOne tck

         Expect.hasLength
            failedWrites
            1
            "should contain 1 billing statement which failed to persist"

         tck.AwaitAssert(
            (fun () ->
               ref <! BillingStatementMessage.GetFailedWrites

               let (failedWrites: BillingStatement seq) = ATK.receiveOne tck

               Expect.hasLength
                  failedWrites
                  0
                  "should contain 0 billing statements after retry"),
            retryPersistenceAfter.Add(TimeSpan.FromSeconds 1)
         )
   ]

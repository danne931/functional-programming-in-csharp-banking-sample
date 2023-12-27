module BillingStatementActorTests

open System
open System.Threading
open Expecto
open Akkling
open FsToolkit.ErrorHandling

module ATK = Akkling.TestKit

open Bank.Account.Domain
open BillingStatement
open Util

let config () =
   Configuration.parse
      """
      billing-statement-mailbox: {
         mailbox-type: "BillingStatementActor+PriorityMailbox, Billing.App"
      }
      """

let billingPersistence: BillingPersistence = {
   saveBillingStatements = fun statements -> TaskResult.ok [ statements.Length ]
}

let stateAfter2Registrations: BillingStatementActor.BulkWriteState = {
   IsScheduled = true
   Billing = [ Stub.billingStatement; Stub.billingStatement ]
}

let init (tck: TestKit.Tck) =
   let props =
      BillingStatementActor.actorProps billingPersistence

   spawn tck "mock-billing" props

[<Tests>]
let tests =
   testList "Billing Statement Actor" [
      akkaTest
         "RegisterBillingStatement message should accumulate billing statements in actor state"
      <| Some(config ())
      <| fun tck ->
         let ref = init tck

         let msg =
            BillingStatementMessage.RegisterBillingStatement
               Stub.billingStatement

         ref <! msg
         ref <! msg
         ref <! BillingStatementMessage.GetWriteReadyStatements
         ATK.expectMsg tck stateAfter2Registrations |> ignore

      akkaTest "Actor state is reset after PersistBillingStatements message"
      <| Some(config ())
      <| fun tck ->
         let ref = init tck

         let msg =
            BillingStatementMessage.RegisterBillingStatement
               Stub.billingStatement

         ref <! msg
         ref <! msg
         Thread.Sleep(100)
         ref <! BillingStatementMessage.PersistBillingStatements
         ref <! BillingStatementMessage.GetWriteReadyStatements
         ATK.expectMsg tck BillingStatementActor.initState |> ignore

      akkaTest "Actor state is reset after batch size reached"
      <| Some(config ())
      <| fun tck ->
         let ref = init tck

         let msg =
            BillingStatementMessage.RegisterBillingStatement
               Stub.billingStatement

         let maxMinus1 = BillingStatementActor.batchSizeLimit - 1

         for _ in [ 1..maxMinus1 ] do
            ref <! msg

         ref <! BillingStatementMessage.GetWriteReadyStatements
         let res = tck.ExpectMsg<BillingStatementActor.BulkWriteState>()

         Expect.equal
            res.Billing.Length
            maxMinus1
            $"actor should have {maxMinus1} billing statements in state"

         // This message increases the batch size to the limit.
         // Billing statements are persisted & state is restored
         // to its initial state.
         ref <! msg

         ref <! BillingStatementMessage.GetWriteReadyStatements
         ATK.expectMsg tck BillingStatementActor.initState |> ignore
   ]

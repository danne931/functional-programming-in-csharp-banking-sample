module InternalTransferActorTests

open Akka.Actor
open Akkling
open Akkling.Cluster.Sharding

open Expecto

open Util
open ActorUtil
open BankTypes
open Lib.Types
open Bank.Transfer.Domain

let initMockAccountActor (tck: TestKit.Tck) (accountOpt: AccountState option) =
   let handler (ctx: Actor<_>) =
      function
      | ShardEnvelope as envelope ->
         match envelope.Message with
         | :? AccountMessage as msg ->
            match msg with
            | AccountMessage.Lookup ->
               ctx.Sender() <! accountOpt
               ignored ()
            | AccountMessage.StateChange msg ->
               match msg with
               | :? DepositTransferCommand
               | :? RejectTransferCommand
               | :? ApproveTransferCommand as cmd ->
                  tck.TestActor.Tell cmd
                  ignored ()
               | msg -> unhandled msg
            | msg -> unhandled msg
         | msg -> unhandled msg

   spawnAnonymous tck <| props (actorOf2 handler)

let initInternalTransferActor
   (tck: TestKit.Tck)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   spawn tck ActorMetadata.internalTransfer.Name
   <| InternalTransferRecipientActor.actorProps getAccountRef

[<Tests>]
let tests =
   testList "Internal Transfer Actor" [
      akkaTest
         "Issuing a transfer to a non-existing account should reject the transfer"
      <| None
      <| fun tck ->
         let mockAccountRef = initMockAccountActor tck None

         let ref =
            initInternalTransferActor tck <| getAccountEntityRef mockAccountRef

         ref <! Stub.event.internalTransferPending

         tck.ExpectMsg<RejectTransferCommand>() |> ignore

      akkaTest
         "Issuing a transfer to a closed account should reject the transfer"
      <| None
      <| fun tck ->
         let mockAccountRef = initMockAccountActor tck None

         let ref =
            initInternalTransferActor tck <| getAccountEntityRef mockAccountRef

         ref <! Stub.event.internalTransferPending

         tck.ExpectMsg<RejectTransferCommand>() |> ignore

      akkaTest
         "Issuing a transfer to an active account should approve the transfer"
      <| None
      <| fun tck ->
         let account = {
            AccountState.empty with
               Status = AccountStatus.Active
         }

         let mockAccountRef = initMockAccountActor tck <| Some account

         let ref =
            initInternalTransferActor tck <| getAccountEntityRef mockAccountRef

         let evt = Stub.event.internalTransferPending
         ref <! evt

         let msg = tck.ExpectMsg<ApproveTransferCommand>()

         Expect.equal
            msg.EntityId
            evt.EntityId
            $"EntityId from TransferPending event should be
            EntityId of resulting ApproveTransferCommand"

         let msg = tck.ExpectMsg<DepositTransferCommand>()

         Expect.equal
            (string msg.EntityId)
            evt.Data.Recipient.Identification
            $"Recipient ID from TransferPending event recipient should be
            EntityId of resulting DepositTransferCommand"

         Expect.equal
            msg.Amount
            evt.Data.DebitedAmount
            $"Debit amount from TransferPending event should
            equal deposit amount of resulting DepositTransferCommand"
   ]

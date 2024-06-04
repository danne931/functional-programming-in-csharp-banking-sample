module InternalTransferActorTests

open Akka.Actor
open Akkling
open Akkling.Cluster.Sharding

open Expecto

open Util
open ActorUtil
open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Transfer.Domain

type private InternalTransferMessage =
   InternalTransferRecipientActor.InternalTransferMessage

let initMockAccountActor (tck: TestKit.Tck) (accountOpt: Account option) =
   let handler (ctx: Actor<_>) (msg: obj) =
      match msg with
      | :? ShardEnvelope as envelope ->
         match envelope.Message with
         | :? AccountMessage as msg ->
            match msg with
            | AccountMessage.GetAccount ->
               ctx.Sender() <! accountOpt
               ignored ()
            | AccountMessage.StateChange msg ->
               match msg with
               | DepositTransfer cmd ->
                  tck.TestActor.Tell cmd
                  ignored ()
               | RejectInternalTransfer cmd ->
                  tck.TestActor.Tell cmd
                  ignored ()
               | ApproveInternalTransfer cmd ->
                  tck.TestActor.Tell cmd
                  ignored ()
               | msg -> unhandled msg
            | msg -> unhandled msg
         | msg -> unhandled msg
      | msg -> unhandled msg

   spawnAnonymous tck <| props (actorOf2 handler)

let initInternalTransferActor
   (tck: TestKit.Tck)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
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

         ref
         <! InternalTransferMessage.TransferRequest
               Stub.event.internalTransferPending

         tck.ExpectMsg<RejectInternalTransferCommand>() |> ignore

      akkaTest
         "Issuing a transfer to a closed account should reject the transfer"
      <| None
      <| fun tck ->
         let mockAccountRef = initMockAccountActor tck None

         let ref =
            initInternalTransferActor tck <| getAccountEntityRef mockAccountRef

         ref
         <! InternalTransferMessage.TransferRequest
               Stub.event.internalTransferPending


         tck.ExpectMsg<RejectInternalTransferCommand>() |> ignore

      akkaTest
         "Issuing a transfer to an active account should approve the transfer"
      <| None
      <| fun tck ->
         let account = Stub.accountStateAfterCreate

         let mockAccountRef = initMockAccountActor tck <| Some account

         let ref =
            initInternalTransferActor tck <| getAccountEntityRef mockAccountRef

         let transferRequest = Stub.event.internalTransferPending
         ref <! InternalTransferMessage.TransferRequest transferRequest

         let msg = tck.ExpectMsg<ApproveInternalTransferCommand>()

         Expect.equal
            (AccountId.fromEntityId msg.EntityId)
            (AccountId.fromEntityId transferRequest.EntityId)
            $"EntityId from Transfer Transaction should be
            EntityId of resulting ApproveTransferCommand"

         let msg = tck.ExpectMsg<DepositTransferCommand>()

         (*
         Expect.equal
            (string msg.EntityId)
            txn.Recipient.Identification
            $"Recipient ID from TransferPending event recipient should be
            EntityId of resulting DepositTransferCommand"
         *)

         Expect.equal
            msg.Data.Amount
            transferRequest.Data.Amount
            $"Debit amount from TransferPending event should
            equal deposit amount of resulting DepositTransferCommand"
   ]

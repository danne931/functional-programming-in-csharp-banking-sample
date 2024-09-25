[<RequireQualifiedAccess>]
module InternalTransferRecipientActor

open Akkling
open Akkling.Cluster.Sharding

open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes

type private DeclinedReason = InternalTransferDeclinedReason

[<RequireQualifiedAccess>]
type InternalTransferMessage =
   | AutomatedTransferRequest of BankEvent<InternalAutomatedTransferPending>
   | TransferRequestWithinOrg of BankEvent<InternalTransferWithinOrgPending>
   | TransferRequestBetweenOrgs of BankEvent<InternalTransferBetweenOrgsPending>

let private declineTransferWithinOrgMsg
   (evt: BankEvent<InternalTransferWithinOrgPending>)
   (reason: DeclinedReason)
   =
   let info = evt.Data.BaseInfo

   RejectInternalTransferWithinOrgCommand.create
      (info.Sender.AccountId, info.Sender.OrgId)
      evt.CorrelationId
      evt.InitiatedById
      { BaseInfo = info; Reason = reason }
   |> AccountCommand.RejectInternalTransfer
   |> AccountMessage.StateChange

let private declineTransferBetweenOrgsMsg
   (evt: BankEvent<InternalTransferBetweenOrgsPending>)
   (reason: DeclinedReason)
   =
   let info = evt.Data.BaseInfo

   RejectInternalTransferBetweenOrgsCommand.create
      (info.Sender.AccountId, evt.OrgId)
      evt.CorrelationId
      evt.InitiatedById
      { BaseInfo = info; Reason = reason }
   |> AccountCommand.RejectInternalTransferBetweenOrgs
   |> AccountMessage.StateChange

let private declineAutoTransferMsg
   (evt: BankEvent<InternalAutomatedTransferPending>)
   (reason: DeclinedReason)
   =
   let info = evt.Data.BaseInfo

   RejectInternalAutoTransferCommand.create
      (info.Sender.AccountId, info.Sender.OrgId)
      evt.CorrelationId
      evt.InitiatedById
      {
         Rule = evt.Data.Rule
         BaseInfo = info
         Reason = reason
      }
   |> AccountCommand.RejectInternalAutoTransfer
   |> AccountMessage.StateChange

let private approveTransferWithinOrgMsg
   (evt: BankEvent<InternalTransferWithinOrgPending>)
   =
   let info = evt.Data.BaseInfo

   ApproveInternalTransferWithinOrgCommand.create
      (info.Sender.AccountId, evt.OrgId)
      evt.CorrelationId
      evt.InitiatedById
      { BaseInfo = info }
   |> AccountCommand.ApproveInternalTransfer
   |> AccountMessage.StateChange

let private approveTransferBetweenOrgsMsg
   (evt: BankEvent<InternalTransferBetweenOrgsPending>)
   =
   let info = evt.Data.BaseInfo

   ApproveInternalTransferBetweenOrgsCommand.create
      (info.Sender.AccountId, evt.OrgId)
      evt.CorrelationId
      evt.InitiatedById
      { BaseInfo = info }
   |> AccountCommand.ApproveInternalTransferBetweenOrgs
   |> AccountMessage.StateChange

let private approveAutoTransferMsg
   (evt: BankEvent<InternalAutomatedTransferPending>)
   =
   let info = evt.Data.BaseInfo

   ApproveInternalAutoTransferCommand.create
      (info.Sender.AccountId, evt.OrgId)
      evt.CorrelationId
      evt.InitiatedById
      {
         Rule = evt.Data.Rule
         BaseInfo = info
      }
   |> AccountCommand.ApproveInternalAutoTransfer
   |> AccountMessage.StateChange

let actorProps
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   : Props<InternalTransferMessage>
   =
   let handler (ctx: Actor<_>) (msg: InternalTransferMessage) = actor {
      let logWarning = logWarning ctx

      let declineTransferMsg =
         match msg with
         | InternalTransferMessage.TransferRequestWithinOrg e ->
            declineTransferWithinOrgMsg e
         | InternalTransferMessage.TransferRequestBetweenOrgs e ->
            declineTransferBetweenOrgsMsg e
         | InternalTransferMessage.AutomatedTransferRequest e ->
            declineAutoTransferMsg e

      let approveTransferMsg () =
         match msg with
         | InternalTransferMessage.TransferRequestWithinOrg e ->
            approveTransferWithinOrgMsg e
         | InternalTransferMessage.TransferRequestBetweenOrgs e ->
            approveTransferBetweenOrgsMsg e
         | InternalTransferMessage.AutomatedTransferRequest e ->
            approveAutoTransferMsg e

      let info, meta =
         match msg with
         | InternalTransferMessage.TransferRequestWithinOrg e ->
            e.Data.BaseInfo,
            {|
               CorrId = e.CorrelationId
               InitiatedBy = e.InitiatedById
            |}
         | InternalTransferMessage.TransferRequestBetweenOrgs e ->
            e.Data.BaseInfo,
            {|
               CorrId = e.CorrelationId
               InitiatedBy = e.InitiatedById
            |}
         | InternalTransferMessage.AutomatedTransferRequest e ->
            e.Data.BaseInfo,
            {|
               CorrId = e.CorrelationId
               InitiatedBy = e.InitiatedById
            |}

      let recipientId = info.Recipient.AccountId
      let senderId = info.Sender.AccountId
      let recipientAccountRef = getAccountRef recipientId
      let senderAccountRef = getAccountRef senderId

      let! (recipientAccountOpt: Account option) =
         recipientAccountRef <? AccountMessage.GetAccount

      match recipientAccountOpt with
      | None ->
         logWarning $"Transfer recipient not found {recipientId}"

         senderAccountRef
         <! declineTransferMsg DeclinedReason.InvalidAccountInfo
      | Some recipientAccount ->
         if recipientAccount.Status = AccountStatus.Closed then
            logWarning $"Transfer recipient account closed"

            senderAccountRef <! declineTransferMsg DeclinedReason.AccountClosed
         else
            senderAccountRef <! approveTransferMsg ()

            // CorrelationId (here as txn.TransactionId) from sender
            // transfer request traced to receiver's deposit.
            let msg =
               match msg with
               | InternalTransferMessage.TransferRequestWithinOrg _ ->
                  DepositInternalTransferWithinOrgCommand.create
                     recipientAccount.CompositeId
                     meta.CorrId
                     meta.InitiatedBy
                     { BaseInfo = info }
                  |> AccountCommand.DepositTransferWithinOrg
                  |> AccountMessage.StateChange
               | InternalTransferMessage.TransferRequestBetweenOrgs _ ->
                  DepositInternalTransferBetweenOrgsCommand.create
                     recipientAccount.CompositeId
                     meta.CorrId
                     meta.InitiatedBy
                     { BaseInfo = info }
                  |> AccountCommand.DepositTransferBetweenOrgs
                  |> AccountMessage.StateChange
               | InternalTransferMessage.AutomatedTransferRequest evt ->
                  DepositInternalAutoTransferCommand.create
                     recipientAccount.CompositeId
                     meta.CorrId
                     meta.InitiatedBy
                     {
                        BaseInfo = info
                        Rule = evt.Data.Rule
                     }
                  |> AccountCommand.DepositInternalAutoTransfer
                  |> AccountMessage.StateChange

            recipientAccountRef <! msg
   }

   props <| actorOf2 handler

let getOrStart
   mailbox
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   =
   ActorMetadata.internalTransfer.Name
   |> getChildActorRef<_, InternalTransferMessage> mailbox
   |> Option.defaultWith (fun _ ->
      spawn mailbox ActorMetadata.internalTransfer.Name
      <| actorProps getAccountRef)

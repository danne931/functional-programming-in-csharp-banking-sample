[<RequireQualifiedAccess>]
module SchedulingActor

open Akka.Hosting
open Akka.Actor
open Akka.Quartz.Actor.Commands
open Akkling

open Bank.Account.Domain
open Bank.Transfer.Domain
open BillingStatement
open ActorUtil
open Lib.SharedTypes
open Lib.Postgres

// NOTE:
// Using a QuartzMessageEnvelope type for messages serialized with
// Akka.Quartz.Actor until serialization PR merged.  Akka.Quartz.Actor
// is always passing in Object as manifest unless this PR merged:
// https://github.com/akkadotnet/Akka.Quartz.Actor/pull/335
type QuartzMessageEnvelope = { Manifest: string; Message: obj }

type Message =
   | AccountClosureCronJobSchedule
   | BillingCycleCronJobSchedule
   | DeleteAccountsJobSchedule of AccountId list
   | TransferProgressCronJobSchedule
   | BalanceHistoryCronJobSchedule
   | TriggerBalanceHistoryCronJob

let actorProps (quartzPersistentActorRef: IActorRef) =
   let handler (ctx: Actor<Message>) = actor {
      let logInfo = logInfo ctx
      let! msg = ctx.Receive()

      match msg with
      | AccountClosureCronJobSchedule ->
         logInfo $"Scheduling nightly account closure checker"

         let trigger = AccountClosureTriggers.scheduleNightlyCheck logInfo
         let path = ActorMetadata.accountClosure.ProxyPath

         let job =
            CreatePersistentJob(
               path,
               {
                  Manifest = "AccountClosureActorMessage"
                  Message = AccountClosureMessage.ScheduleDeleteAll
               },
               trigger
            )

         quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
         ignored ()
      | BillingCycleCronJobSchedule ->
         logInfo $"Scheduling monthly billing cycle"

         let trigger = BillingCycleTriggers.scheduleMonthly logInfo
         let path = ActorMetadata.billingCycle.ProxyPath

         let job =
            CreatePersistentJob(
               path,
               {
                  Manifest = "BillingCycleActorMessage"
                  Message = BillingCycleMessage.BillingCycleFanout
               },
               trigger
            )

         quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
         ignored ()
      | DeleteAccountsJobSchedule accountIds ->
         logInfo $"Scheduling deletion of accounts {accountIds}"

         let trigger = AccountClosureTriggers.deleteAccounts logInfo
         let path = ActorMetadata.accountClosure.ProxyPath

         let job =
            CreatePersistentJob(
               path,
               {
                  Manifest = "AccountClosureActorMessage"
                  Message = AccountClosureMessage.DeleteAll accountIds
               },
               trigger
            )

         quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
         ignored ()
      | TransferProgressCronJobSchedule ->
         logInfo $"Scheduling transfer progress tracking."

         let trigger = TransferProgressTrackingTriggers.schedule logInfo
         let path = ActorMetadata.transferProgressTracking.ProxyPath

         let job =
            CreatePersistentJob(
               path,
               {
                  Manifest = "TransferProgressTrackingActorMessage"
                  Message = TransferProgressTrackingMessage.ProgressCheck
               },
               trigger
            )

         quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
         ignored ()
      | BalanceHistoryCronJobSchedule ->
         logInfo "Scheduling daily balance history update."
         let trigger = BalanceHistoryTriggers.scheduleNightly logInfo
         let path = ctx.Self.Path

         let job =
            CreatePersistentJob(
               path,
               {
                  Manifest = "SchedulingActorMessage"
                  Message = Message.TriggerBalanceHistoryCronJob
               },
               trigger
            )

         quartzPersistentActorRef.Tell(job)
         ignored ()
      | TriggerBalanceHistoryCronJob ->
         logInfo "Balance history daily update triggered."
         let! res = pgProcedure "update_balance_history_for_yesterday" None

         match res with
         | Ok _ ->
            logInfo $"Balance history daily update complete."
            return ignored ()
         | Error err ->
            logError ctx $"Daily balance history update error: {err}"
            return unhandled ()
   }

   props handler

let get (registry: IActorRegistry) : IActorRef<Message> =
   typed <| registry.Get<ActorMetadata.SchedulingMarker>()

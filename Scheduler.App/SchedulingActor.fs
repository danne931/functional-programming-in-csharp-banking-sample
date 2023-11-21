[<RequireQualifiedAccess>]
module SchedulingActor

open System
open Akka.Hosting
open Akka.Actor
open Akka.Quartz.Actor.Commands
open Akkling

open Bank.Account.Domain
open BillingStatement
open ActorUtil

type Message =
   | AccountClosureCronJobSchedule
   | BillingCycleCronJobSchedule
   | DeleteAccountsJobSchedule of Guid list

let actorProps (quartzPersistentActorRef: IActorRef) =
   let handler (ctx: Actor<Message>) =
      function
      | AccountClosureCronJobSchedule ->
         logInfo ctx $"Scheduling nightly account closure checker"

         let trigger = AccountClosureTriggers.scheduleNightlyCheck ()
         let path = ActorMetadata.accountClosure.ProxyPath.Value

         let job =
            CreatePersistentJob(
               path,
               AccountClosureMessage.ScheduleDeleteAll,
               trigger
            )

         quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
         ignored ()
      | BillingCycleCronJobSchedule ->
         logInfo ctx $"Scheduling monthly billing cycle"

         let trigger = BillingCycleTriggers.scheduleMonthly ()
         let path = ActorMetadata.billingCycleBulkWrite.ProxyPath.Value

         let job =
            CreatePersistentJob(
               path,
               BillingMessage.BillingCycleFanout,
               trigger
            )

         quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
         ignored ()
      | DeleteAccountsJobSchedule accountIds ->
         logInfo ctx $"Scheduling deletion of accounts {accountIds}"

         let trigger = AccountClosureTriggers.deleteAccounts ()
         let path = ActorMetadata.accountClosure.ProxyPath.Value

         let job =
            CreatePersistentJob(
               path,
               AccountClosureMessage.DeleteAll accountIds,
               trigger
            )

         quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
         ignored ()

   props <| actorOf2 handler

let get (registry: IActorRegistry) : IActorRef<Message> =
   typed <| registry.Get<ActorMetadata.SchedulingMarker>()

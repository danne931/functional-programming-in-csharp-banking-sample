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

// NOTE:
// Using a QuartzMessageEnvelope type for messages serialized with
// Akka.Quartz.Actor until serialization PR merged.  Akka.Quartz.Actor
// is always passing in Object as manifest unless this PR merged:
// https://github.com/akkadotnet/Akka.Quartz.Actor/pull/335
type QuartzMessageEnvelope = { Manifest: string; Message: obj }

type Message =
   | AccountClosureCronJobSchedule
   | BillingCycleCronJobSchedule
   | DeleteAccountsJobSchedule of Guid list

let actorProps (quartzPersistentActorRef: IActorRef) =
   let handler (ctx: Actor<Message>) =
      let logInfo = logInfo ctx

      function
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
                  Message = BillingMessage.BillingCycleFanout
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

   props <| actorOf2 handler

let get (registry: IActorRegistry) : IActorRef<Message> =
   typed <| registry.Get<ActorMetadata.SchedulingMarker>()

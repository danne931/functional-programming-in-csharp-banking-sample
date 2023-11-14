[<RequireQualifiedAccess>]
module BillingCycleActor

open System
open Quartz
open Akka.Actor
open Akka.Streams
open Akka.Quartz.Actor.Commands
open Akkling

open Bank.Account.Domain
open ActorUtil

type Message = | BillingCycle

let scheduleMonthly
   (system: ActorSystem)
   (quartzPersistentActorRef: IActorRef)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   let name = "BillingCycle"
   let group = "Bank"

   let trigger =
      TriggerBuilder
         .Create()
         .ForJob($"{name}Job", group)
         .WithIdentity($"{name}Trigger", group)
         .WithDescription(
            "Accounts transactions are consolidated at the end of a billing cycle."
         )
#if DEBUG
         .WithSimpleSchedule(fun s ->
            s.WithIntervalInMinutes(2).RepeatForever() |> ignore)
#else
         .WithCronSchedule("0 55 23 L * ? *") // Last day of every month at 11:55PM
#endif
         .Build()

   let publisherHandler (msg: Message) =
      ignored
      <| ActorUtil
         .readJournal(system)
         .CurrentPersistenceIds()
         .RunForeach(
            (fun id ->
               try
                  getAccountRef <| Guid id <! AccountMessage.BillingCycle
               with _ ->
                  ()),
            system.Materializer()
         )

   let publisherAref =
      spawn system $"{name}Publisher" <| props (actorOf publisherHandler)

   let job =
      CreatePersistentJob(publisherAref.Path, Message.BillingCycle, trigger)

   quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
   ()

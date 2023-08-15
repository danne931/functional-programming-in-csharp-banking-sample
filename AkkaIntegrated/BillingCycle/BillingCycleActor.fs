[<RequireQualifiedAccess>]
module BillingCycleActor

open System
open Quartz
open Akka.Actor
open Akka.Streams
open Akka.Quartz.Actor.Commands
open Akkling

open BankTypes
open Bank.Account.Domain
open BillingStatement
open Bank.BillingCycle.Api
open ActorUtil

type Message =
   private
   | SaveBillingStatement
   | Broadcast of BillingStatement

let start
   (mailbox: Actor<obj>)
   (persistence: AccountPersistence)
   (account: AccountState)
   =
   let actorName = ActorMetadata.billingCycle.Name
   let accountId = account.EntityId

   let handler (ctx: Actor<Message>) = actor {
      let! msg = ctx.Receive()
      let aref = ctx.Parent()

      match msg with
      | SaveBillingStatement ->
         let! txnsOpt = persistence.getEvents accountId |> Async.AwaitTask

         if txnsOpt.IsNone then
            printfn "Oops... No accounts found for billing cycle."
         else
            let! statement =
               saveBillingStatement account txnsOpt.Value |> Async.AwaitTask

            ctx.Self <! Broadcast statement
      | Broadcast billingStatement ->
         // Maintenance fee conditionally applied after account transactions
         // have been consolidated. If applied, it will be the first transaction
         // of the new billing cycle.
         aref <! AccountMessage.BillingCycleEnd

         let criteria = account.MaintenanceFeeCriteria

         if
            criteria.QualifyingDepositFound || criteria.DailyBalanceThreshold
         then
            let cmd = SkipMaintenanceFeeCommand(accountId, criteria)
            aref <! AccountMessage.StateChange cmd
         else
            let cmd = MaintenanceFeeCommand accountId
            aref <! AccountMessage.StateChange cmd

         select mailbox (string ActorMetadata.email.Path.Value)
         <! EmailActor.BillingStatement billingStatement

         retype ctx.Self <! PoisonPill.Instance
   }

   let aref = spawn mailbox actorName <| props handler
   aref <! SaveBillingStatement
   aref

let scheduleMonthly
   (system: ActorSystem)
   (quartzPersistentActorRef: IActorRef)
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

   let publisherHandler (msg: BillingCycleCommand) =
      ignored
      <| ActorUtil
         .readJournal(system)
         .CurrentPersistenceIds()
         .RunForeach(
            (fun id ->
               if id.StartsWith("account/") then
                  let id = id.Split("/")[2] |> Guid
                  let fac = ActorUtil.AccountActorFac system
                  fac.tell id <| AccountMessage.BillingCycle msg),
            system.Materializer()
         )

   let publisherAref =
      spawn system $"{name}Publisher" <| props (actorOf publisherHandler)

   let job =
      CreatePersistentJob(publisherAref.Path, BillingCycleCommand(), trigger)

   quartzPersistentActorRef.Tell(job, ActorRefs.NoSender)
   ()

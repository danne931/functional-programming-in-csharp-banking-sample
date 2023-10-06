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
open ActorUtil

module Api = Bank.BillingCycle.Api

type Message =
   | SaveBillingStatement
   | AccountTransactionsLoaded of AccountEvent list option

let actorProps
   (persistence: AccountPersistence)
   (account: AccountState)
   (billingCycleBulkWriteActor: IActorRef<BillingCycleBulkWriteActor.Message>)
   (emailActor: IActorRef<EmailActor.EmailMessage>)
   =
   let accountId = account.EntityId

   let getTransactions () = async {
      let! txnsOpt = persistence.getEvents accountId |> Async.AwaitTask
      return AccountTransactionsLoaded txnsOpt
   }

   let handler (ctx: Actor<Message>) = actor {
      let! msg = ctx.Receive()
      let accountRef = ctx.Parent()

      match msg with
      | SaveBillingStatement -> getTransactions () |!> ctx.Self
      | AccountTransactionsLoaded txnsOpt ->
         if txnsOpt.IsNone then
            logWarning ctx "No transactions found for billing cycle."
         else
            let billing = BillingStatement.create account txnsOpt.Value

            billingCycleBulkWriteActor
            <! BillingCycleBulkWriteActor.RegisterBillingStatement billing

            // Maintenance fee conditionally applied after account transactions
            // have been consolidated. If applied, it will be the first transaction
            // of the new billing cycle.
            accountRef <! AccountMessage.BillingCycleEnd

            let criteria = account.MaintenanceFeeCriteria

            if
               criteria.QualifyingDepositFound || criteria.DailyBalanceThreshold
            then
               let cmd = SkipMaintenanceFeeCommand(accountId, criteria)
               accountRef <! AccountMessage.StateChange cmd
            else
               let cmd = MaintenanceFeeCommand accountId
               accountRef <! AccountMessage.StateChange cmd

            emailActor <! EmailActor.BillingStatement account

         retype ctx.Self <! PoisonPill.Instance
   }

   props handler


let start
   (mailbox: Actor<obj>)
   (persistence: AccountPersistence)
   (account: AccountState)
   =
   let aref =
      spawn mailbox ActorMetadata.billingCycle.Name
      <| actorProps
            persistence
            account
            (BillingCycleBulkWriteActor.get mailbox.System)
            (EmailActor.get mailbox.System)

   aref <! SaveBillingStatement
   aref

let scheduleMonthly
   (system: ActorSystem)
   (quartzPersistentActorRef: IActorRef)
   (getAccountRef: EntityRefGetter<obj>)
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
               let accountIdOpt =
                  try
                     Some <| Guid id
                  with _ ->
                     None

               if accountIdOpt.IsSome then
                  getAccountRef accountIdOpt.Value
                  <! AccountMessage.BillingCycle msg),
            system.Materializer()
         )

   let publisherAref =
      spawn system $"{name}Publisher" <| props (actorOf publisherHandler)

   let job =
      CreatePersistentJob(publisherAref.Path, BillingCycleCommand(), trigger)

   quartzPersistentActorRef.Tell(job, ActorRefs.Nobody)
   ()

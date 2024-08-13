[<RequireQualifiedAccess>]
module BillingCycleActor

open Akka.Actor
open Akka.Hosting
open Akka.Streams
open Akkling.Streams
open Akkling
open Akkling.Cluster.Sharding
open System

open BillingStatement
open Bank.Account.Domain
open AccountSqlMapper
open ActorUtil
open Lib.Types
open Lib.Postgres
open Lib.SharedTypes

let getBillingCycleReadyAccounts () =
   let prevCycle = AccountFields.lastBillingCycleDate

   let lookback =
      if Env.isProd then
         "'27 days'::interval"
      else
         "'1 minutes'::interval"

   pgQuery<AccountId * OrgId>
      $"""
      SELECT {AccountFields.accountId}, {AccountFields.orgId}
      FROM {AccountSqlMapper.table}
      WHERE
         {AccountFields.status} = '{string AccountStatus.Active}'
         AND ({prevCycle} IS NULL
              OR {prevCycle} < current_timestamp - {lookback})
      """
      None
   <| fun read -> AccountSqlReader.accountId read, AccountSqlReader.orgId read

let private fanOutBillingCycleMessage
   (ctx: Actor<_>)
   (throttle: StreamThrottle)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   =
   task {
      let mat = ctx.System.Materializer()

      do!
         getBillingCycleReadyAccounts ()
         |> Async.AwaitTask
         |> Source.ofAsync
         |> Source.throttle
               ThrottleMode.Shaping
               throttle.Burst
               throttle.Count
               throttle.Duration
         |> Source.choose (fun res ->
            match res with
            | Error e ->
               logError ctx $"Error fetching active account ids {e}"
               None
            | Ok opt ->
               if opt.IsNone then
                  logError ctx "No active accounts."

               opt)
         |> Source.collect id
         |> Source.runForEach mat (fun (accountId, orgId) ->
            // This billing cycle actor is scheduled to run at the start of
            // every month.  The billing period refers to the previous month.
            let billingPeriod = DateTime.UtcNow.AddMonths -1

            let msg =
               StartBillingCycleCommand.create (accountId, orgId) {
                  Month = billingPeriod.Month
                  Year = billingPeriod.Year
                  Reference = None
               }
               |> AccountCommand.StartBillingCycle
               |> AccountMessage.StateChange

            getAccountRef accountId <! msg)

      return BillingCycleMessage.BillingCycleFinished
   }

let actorProps
   (throttle: StreamThrottle)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   =
   let handler (ctx: Actor<BillingCycleMessage>) =
      function
      | BillingCycleMessage.BillingCycleFanout ->
         logInfo ctx "Start billing cycle"

         fanOutBillingCycleMessage ctx throttle getAccountRef |> Async.AwaitTask
         |!> retype ctx.Self
         |> ignored
      | BillingCycleMessage.BillingCycleFinished ->
         logInfo ctx "Billing cycle finished"
         ignored ()

   props <| actorOf2 handler

let get (system: ActorSystem) : IActorRef<BillingCycleMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.BillingCycleMarker>()

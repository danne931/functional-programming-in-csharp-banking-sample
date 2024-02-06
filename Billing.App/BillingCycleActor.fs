[<RequireQualifiedAccess>]
module BillingCycleActor

open System
open Akka.Actor
open Akka.Hosting
open Akka.Streams
open Akkling.Streams
open Akkling

open BillingStatement
open Bank.Account.Domain
open AccountSqlMapper
open ActorUtil
open Lib.Types
open Lib.Postgres

let getActiveAccounts () =
   let prevCycle = AccountFields.lastBillingCycleDate

   let lookback =
      if Env.isDev then
         "'1 minutes'::interval"
      else
         "'27 days'::interval"

   pgQuery<Guid * decimal>
      $"""
      SELECT {AccountFields.entityId}, {AccountFields.balance}
      FROM accounts
      WHERE
         {AccountFields.status} = '{string AccountStatus.Active}'
         AND ({prevCycle} IS NULL
              OR {prevCycle} < current_timestamp - {lookback})
      """
      None
   <| fun read -> AccountSqlReader.entityId read, AccountSqlReader.balance read

let private fanOutBillingCycleMessage
   (ctx: Actor<_>)
   (throttle: StreamThrottle)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   task {
      let mat = ctx.System.Materializer()

      do!
         getActiveAccounts ()
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
               logError ctx $"Error fetching active account ids"
               None
            | Ok opt ->
               if opt.IsNone then
                  logError ctx "No active accounts."

               opt)
         |> Source.collect id
         |> Source.runForEach mat (fun (accountId, balance) ->
            let msg =
               StartBillingCycleCommand(accountId, balance)
               |> AccountCommand.StartBillingCycle
               |> AccountMessage.StateChange

            getAccountRef accountId <! msg)

      return BillingCycleFinished
   }

let actorProps
   (throttle: StreamThrottle)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   let handler (ctx: Actor<BillingCycleMessage>) =
      function
      | BillingCycleFanout ->
         logInfo ctx "Start billing cycle"

         fanOutBillingCycleMessage ctx throttle getAccountRef |> Async.AwaitTask
         |!> retype ctx.Self
         |> ignored
      | BillingCycleFinished ->
         logInfo ctx "Billing cycle finished"
         ignored ()

   props <| actorOf2 handler

let get (system: ActorSystem) : IActorRef<BillingCycleMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.BillingCycleMarker>()

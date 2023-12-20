[<RequireQualifiedAccess>]
module BillingCycleActor

open System
open Akka.Actor
open Akka.Hosting
open Akka.Dispatch
open Akka.Configuration
open Akka.Streams
open Akkling

open BillingStatement
open Bank.BillingCycle.Api
open Bank.Account.Domain
open ActorUtil

let private forwardToAccount
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (id: string)
   =
   let idOpt =
      try
         Guid id |> Some
      with _ ->
         None

   if idOpt.IsSome then
      getAccountRef idOpt.Value <! AccountMessage.BillingCycle

let private fanOutBillingCycleMessage
   system
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   task {
      do!
         ActorUtil
            .readJournal(system)
            .CurrentPersistenceIds()
            .RunForeach(forwardToAccount getAccountRef, system.Materializer())

      return BillingCycleFinished
   }

// Configure mailbox so that PersistBillingStatements
// messages are sent to the front.
type PriorityMailbox(settings: Settings, config: Config) =
   inherit UnboundedPriorityMailbox(settings, config)

   override x.PriorityGenerator(message: obj) =
      match message with
      | :? BillingMessage as msg ->
         match msg with
         | PersistBillingStatements -> 0
         | PersistBillingStatementsResponse _ -> 1
         | RegisterBillingStatement _ -> 2
         | GetWriteReadyStatements -> 3
         | BillingCycleFanout -> 4
         | BillingCycleFinished -> 5
      | _ -> 33

type BulkWriteState = {
   Billing: BillingStatement list
   IsScheduled: bool
}

let private schedulePersist (ctx: Actor<BillingMessage>) (seconds: float) =
   ctx.Schedule (TimeSpan.FromSeconds seconds) ctx.Self PersistBillingStatements
   |> ignore

let batchSizeLimit = 1000
let initState = { Billing = []; IsScheduled = false }

// Collects billing statement records to insert in batches.
// Billing statements are persisted in a single transaction
// once the batch size limit is reached or after some duration
// in seconds from the initial RegisterBillingStatement message.
let actorProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (persistence: BillingPersistence)
   (broadcaster: AccountBroadcast)
   =
   let handler (ctx: Actor<BillingMessage>) =
      let schedulePersist = schedulePersist ctx
      let logInfo, logError = logInfo ctx, logError ctx

      let rec loop (state: BulkWriteState) =
         function
         | GetWriteReadyStatements ->
            ctx.Sender() <! state
            ignored ()
         | RegisterBillingStatement statement ->
            let newState = {
               state with
                  Billing = statement :: state.Billing
            }

            if newState.Billing.Length >= batchSizeLimit then
               ctx.Self <! PersistBillingStatements
               become <| loop newState
            elif state.IsScheduled then
               become <| loop newState
            else
               schedulePersist 5.

               become <| loop { newState with IsScheduled = true }
         | PersistBillingStatements ->
            let saveStatements () = task {
               let! res = persistence.saveBillingStatements state.Billing
               return PersistBillingStatementsResponse res
            }

            if state.Billing.Length > 0 then
               saveStatements () |> Async.AwaitTask |!> ctx.Self

               ignored ()
            else
               become <| loop initState
         | PersistBillingStatementsResponse res ->
            match res with
            | Ok o ->
               logInfo $"Saved billing statements {o}"
               become <| loop initState
            | Error e ->
               logError $"Error saving billing statements {e}"
               schedulePersist 10.
               unhandled ()
         | BillingCycleFanout ->
            logInfo "Start billing cycle"
            broadcaster.endBillingCycle () |> ignore

            fanOutBillingCycleMessage ctx.System getAccountRef
            |> Async.AwaitTask
            |!> retype ctx.Self
            |> ignored
         | BillingCycleFinished ->
            logInfo "Billing cycle finished"
            ignored ()

      loop initState

   {
      (props <| actorOf2 handler) with
         Mailbox = Some "billing-cycle-mailbox"
   }

let get (system: ActorSystem) : IActorRef<BillingMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.BillingCycleMarker>()

let initProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (broadcaster: AccountBroadcast)
   =
   actorProps
      getAccountRef
      {
         saveBillingStatements = saveBillingStatements
      }
      broadcaster

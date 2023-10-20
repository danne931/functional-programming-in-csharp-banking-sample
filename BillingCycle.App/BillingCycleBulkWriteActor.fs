[<RequireQualifiedAccess>]
module BillingCycleBulkWriteActor

open System
open Akka.Actor
open Akka.Hosting
open Akka.Dispatch
open Akka.Configuration
open Akkling

open BillingStatement
open ActorUtil

type Message =
   | RegisterBillingStatement of BillingStatement
   | PersistBillingStatements
   | Lookup

// Configure mailbox so that PersistBillingStatements
// messages are sent to the front.
type PriorityMailbox(settings: Settings, config: Config) =
   inherit UnboundedPriorityMailbox(settings, config)

   override x.PriorityGenerator(message: obj) =
      match message with
      | :? Message as msg ->
         match msg with
         | PersistBillingStatements -> 0
         | RegisterBillingStatement _ -> 1
         | Lookup -> 2
      | _ -> 33

type BulkWriteState = {
   Billing: BillingStatement list
   IsScheduled: bool
}

let private schedulePersist (ctx: Actor<Message>) (seconds: float) =
   ctx.Schedule (TimeSpan.FromSeconds seconds) ctx.Self PersistBillingStatements
   |> ignore

let batchSizeLimit = 1000
let initState = { Billing = []; IsScheduled = false }

// Collects billing statement records to insert in batches.
// Billing statements are persisted in a single transaction
// once the batch size limit is reached or after some duration
// in seconds from the initial RegisterBillingStatement message.
let start (system: ActorSystem) (persistence: BillingPersistence) =
   let handler (ctx: Actor<Message>) =
      let schedulePersist = schedulePersist ctx
      let logInfo, logError = logInfo ctx, logError ctx

      let rec loop (state: BulkWriteState) =
         function
         | Lookup ->
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
            if state.Billing.Length > 0 then
               let res = persistence.saveBillingStatements(state.Billing).Result

               match res with
               | Ok o ->
                  logInfo $"Saved billing statements {o}"
                  become <| loop initState
               | Error e ->
                  logError $"Error saving billing statements {e}"
                  schedulePersist 10.
                  unhandled ()
            else
               become <| loop initState

      loop initState

   spawn system ActorMetadata.billingCycleBulkWrite.Name {
      (props <| actorOf2 handler) with
         Mailbox = Some "billing-cycle-bulk-write-mailbox"
   }

let get (system: ActorSystem) : IActorRef<Message> =
   typed
   <| ActorRegistry.For(system).Get<ActorMetadata.BillingCycleBulkWriteMarker>()

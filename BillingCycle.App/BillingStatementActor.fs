[<RequireQualifiedAccess>]
module BillingStatementActor

open System
open Akka.Actor
open Akka.Hosting
open Akka.Dispatch
open Akka.Configuration
open Akkling

open BillingStatement
open Bank.BillingCycle.Api
open ActorUtil

// Configure mailbox so that PersistBillingStatements
// messages are sent to the front.
type PriorityMailbox(settings: Settings, config: Config) =
   inherit UnboundedPriorityMailbox(settings, config)

   override x.PriorityGenerator(message: obj) =
      match message with
      | :? BillingStatementMessage as msg ->
         match msg with
         | PersistBillingStatements -> 0
         | PersistBillingStatementsResponse _ -> 1
         | RegisterBillingStatement _ -> 2
         | GetWriteReadyStatements -> 3
      | _ -> 33

type BulkWriteState = {
   Billing: BillingStatement list
   IsScheduled: bool
}

let private schedulePersist
   (ctx: Actor<BillingStatementMessage>)
   (seconds: float)
   =
   ctx.Schedule (TimeSpan.FromSeconds seconds) ctx.Self PersistBillingStatements
   |> ignore

let batchSizeLimit = 1000
let initState = { Billing = []; IsScheduled = false }

// Collects billing statement records to insert in batches.
// Billing statements are persisted in a single transaction
// once the batch size limit is reached or after some duration
// in seconds from the initial RegisterBillingStatement message.
let actorProps (persistence: BillingPersistence) =
   let handler (ctx: Actor<BillingStatementMessage>) =
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

      loop initState

   {
      (props <| actorOf2 handler) with
         Mailbox = Some "billing-statement-mailbox"
   }

let get (system: ActorSystem) : IActorRef<BillingStatementMessage> =
   typed
   <| ActorRegistry.For(system).Get<ActorMetadata.BillingStatementMarker>()

let initProps () =
   actorProps {
      saveBillingStatements = saveBillingStatements
   }

let start (system: ActorSystem) =
   spawn system ActorMetadata.billingStatement.Name <| initProps ()

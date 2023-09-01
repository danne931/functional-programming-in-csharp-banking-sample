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

module Api = Bank.BillingCycle.Api

type Message =
   | RegisterBillingStatement of BillingStatement
   | PersistBillingStatements

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
      | _ -> 33

type BulkWriteState = {
   Billing: BillingStatement list
   IsScheduled: bool
}

let private schedulePersist (ctx: Actor<Message>) (seconds: int) =
   ctx.Schedule (TimeSpan.FromSeconds seconds) ctx.Self PersistBillingStatements
   |> ignore

let start (system: ActorSystem) =
   let handler (ctx: Actor<Message>) =
      let rec loop (state: BulkWriteState) =
         function
         | RegisterBillingStatement statement ->
            let newState = {
               state with
                  Billing = statement :: state.Billing
            }

            if newState.Billing.Length >= 1000 then
               ctx.Self <! PersistBillingStatements
               become <| loop newState
            elif state.IsScheduled then
               become <| loop newState
            else
               schedulePersist ctx 5

               become <| loop { newState with IsScheduled = true }
         | PersistBillingStatements ->
            let refreshedState = { Billing = []; IsScheduled = false }

            if state.Billing.Length > 0 then
               let res = Api.saveBillingStatements(state.Billing).Result

               match res with
               | Ok _ -> become <| loop refreshedState
               | Error e ->
                  logError ctx $"Issue saving billing statements {e}"
                  schedulePersist ctx 90
                  unhandled ()
            else
               become <| loop refreshedState

      loop { Billing = []; IsScheduled = false }

   spawn
      system
      ActorMetadata.billingCycleBulkWrite.Name
      {
         (props <| actorOf2 handler) with
            Mailbox = Some "billing-cycle-bulk-write-mailbox"
      }

let get (system: ActorSystem) : IActorRef<Message> =
   typed
   <| ActorRegistry.For(system).Get<ActorMetadata.BillingCycleBulkWriteMarker>()

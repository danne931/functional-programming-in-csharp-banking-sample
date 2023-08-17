[<RequireQualifiedAccess>]
module EmailActor

open Akka.Actor
open Akka.Hosting
open Akkling

open BankTypes
open ActorUtil
open Bank.Account.Domain
open BillingStatement

type EmailMessage =
   | AccountOpen of AccountState
   | AccountClose of AccountState
   | BillingStatement of BillingStatement
   | DailyBalanceThreshold of AccountState
   | LowBalance of AccountState
   | DepositReceived of DepositedCash * AccountState
   | SendgridWebhook //of

let start (system: ActorSystem) : IActorRef<EmailMessage> =
   let handler (ctx: Actor<_>) (msg: EmailMessage) = actor {
      match msg with
      | AccountOpen account ->
         logInfo ctx "send account welcome email"
         ignored ()
      | AccountClose account ->
         logInfo ctx "send account close email"
         ignored ()
      | BillingStatement account ->
         logInfo ctx "send billing statement email"
         ignored ()
      | DailyBalanceThreshold account ->
         logInfo
            ctx
            "user needs to raise threshold to place more purchases for the
      day email"

         ignored ()
      | LowBalance account ->
         logInfo ctx "send low balance warning"
         ignored ()
      | DepositReceived(evt, account) ->
         logInfo ctx "deposit received email"
         ignored ()
      | SendgridWebhook _ ->
         // TODO: Save sendgrid email state such as opened/bounced/dropped etc.
         //       in postgres
         ignored ()
   }

   spawn system ActorMetadata.email.Name (props (actorOf2 handler))

let get (system: ActorSystem) : IActorRef<EmailMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.EmailMarker>()

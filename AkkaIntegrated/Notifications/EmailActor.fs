[<RequireQualifiedAccess>]
module EmailActor

open Akka.Actor
open Akka.Hosting
open Akka.Pattern
open Akkling
open System
open System.Net.Http
open System.Net.Http.Json

open BankTypes
open Lib.ActivePatterns
open Lib.Types
open ActorUtil
open Bank.Transfer.Domain

// NOTE: May have to throttle requests to email service as number of
//       TransferDeposited/BillingStatement messages received per second
//       may be higher than the number of requests per second that
//       the email service will allow.

type EmailMessage =
   | AccountOpen of AccountState
   | AccountClose of AccountState
   | BillingStatement of AccountState
   | DebitDeclined of string * AccountState
   | TransferDeposited of BankEvent<TransferDeposited> * AccountState

type private TrackingEvent = {
   event: string
   email: string
   data: obj
}

let private emailPropsFromMessage (msg: EmailMessage) =
   match msg with
   | AccountOpen account -> {
      event = "account-opened"
      email = account.Email
      data = {| firstName = account.FirstName |}
     }
   | AccountClose account -> {
      event = "account-closed"
      email = account.Email
      data = {| firstName = account.FirstName |}
     }
   // TODO: Include link to view statement
   | BillingStatement account -> {
      event = "billing-statement"
      email = account.Email
      data = {| |}
     }
   | DebitDeclined(reason, account) ->
      let o = {
         event = "debit-declined"
         email = account.Email
         data = {| reason = reason |}
      }

      match reason with
      | Contains "InsufficientBalance" -> {
         o with
            data = {|
               reason =
                  $"Your account has insufficient funds. 
                    Your balance is ${account.Balance}"
            |}
        }
      | Contains "ExceededDailyDebit" -> {
         o with
            data = {|
               reason =
                  $"You have spent ${account.DailyDebitAccrued} today. 
                    Your daily debit limit is set to ${account.DailyDebitLimit}."
            |}
        }
      | _ -> o
   | TransferDeposited(evt, account) -> {
      event = "transfer-deposited"
      email = account.Email
      data = {|
         firstName = account.FirstName
         amount = $"${evt.Data.DepositedAmount}"
         origin = evt.Data.Origin
      |}
     }

// Side effect: Raise an exception instead of returning Result.Error
//              to trip circuit breaker
let private sendEmail (client: HttpClient) (data: TrackingEvent) = task {
   use! response =
      client.PostAsJsonAsync(
         "track",
         {|
            event = data.event
            email = data.email
            data = data.data
         |}
      )

   if not response.IsSuccessStatusCode then
      let! content = response.Content.ReadFromJsonAsync()
      failwith $"Error sending email: {response.ReasonPhrase} - {content}"

   return response
}

let private createClient (bearerToken: string) =
   let client =
      new HttpClient(BaseAddress = Uri("https://api.useplunk.com/v1/"))

   client.DefaultRequestHeaders.Authorization <-
      Headers.AuthenticationHeaderValue("Bearer", bearerToken)

   client

let start
   (system: ActorSystem)
   (breaker: CircuitBreaker)
   (broadcaster: AccountBroadcast)
   : IActorRef<obj>
   =
   let emailBearerToken = Environment.GetEnvironmentVariable("EmailBearerToken")

   let client =
      match isNull emailBearerToken with
      | true -> None
      | false -> Some(createClient emailBearerToken)

   let handler (ctx: Actor<_>) (msg: obj) =
      let logWarning, logError = logWarning ctx, logError ctx

      match msg with
      | :? EmailMessage as msg ->
         let emailData = emailPropsFromMessage msg

         if client.IsNone then
            logWarning "EmailBearerToken not set.  Will not send email."
         else
            breaker.WithCircuitBreaker(fun () ->
               sendEmail client.Value emailData)
            |> Async.AwaitTask
            |!> retype ctx.Self

         ignored ()
      | LifecycleEvent _ -> ignored ()
      | :? HttpResponseMessage ->
         // Successful request to email service -> ignore
         ignored ()
      | :? Status.Failure ->
         // Failed request to email service -> dead letters
         unhandled ()
      | msg ->
         logError $"Unknown msg {msg}"
         unhandled ()

   let ref = spawn system ActorMetadata.email.Name (props <| actorOf2 handler)

   breaker.OnHalfOpen(fun () ->
      broadcaster.broadcastCircuitBreaker
         {
            Service = Service.Email
            Status = CircuitBreakerStatus.HalfOpen
         }
      |> ignore)
   |> ignore

   breaker.OnClose(fun () ->
      broadcaster.broadcastCircuitBreaker
         {
            Service = Service.Email
            Status = CircuitBreakerStatus.Closed
         }
      |> ignore)
   |> ignore

   breaker.OnOpen(fun () ->
      system.Log.Log(
         Akka.Event.LogLevel.WarningLevel,
         null,
         "Email circuit breaker open"
      )

      broadcaster.broadcastCircuitBreaker
         {
            Service = Service.Email
            Status = CircuitBreakerStatus.Open
         }
      |> ignore)
   |> ignore

   ref

let get (system: ActorSystem) : IActorRef<EmailMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.EmailMarker>()
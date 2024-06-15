[<RequireQualifiedAccess>]
module EmailActor

open Akka.Actor
open Akka.Hosting
open Akkling
open Akka.Streams
open Akkling.Streams
open System
open System.Net.Http
open System.Net.Http.Json
open FsToolkit.ErrorHandling

open Lib.ActivePatterns
open Lib.Types
open Lib.SharedTypes
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.Employee.Domain

// TODO: Comment out all account related email messages until
//       I associate account owners with the account.
//
type EmailMessage =
   | AccountOpen of Account
   | AccountClose of Account
   | BillingStatement of Account
   | DebitDeclinedExceededDailyDebit of
      limit: decimal *
      accrued: decimal *
      Email
   | DebitDeclinedInsufficientAccountBalance of balance: decimal * Email
   | TransferDeposited of BankEvent<TransferDeposited> * Account
   | ApplicationErrorRequiresSupport of string

type private CircuitBreakerMessage =
   | BreakerHalfOpen
   | BreakerClosed

type private QueueItem = QueueItem of EmailMessage

type private TrackingEvent = {
   event: string
   email: string
   data: obj
}

let private emailPropsFromMessage (msg: EmailMessage) =
   match msg with
   | AccountOpen account -> {
      event = "account-opened"
      //email = string account.Email
      //data = {| firstName = account.FirstName |}
      email = ""
      data = {| |}
     }
   | AccountClose account -> {
      event = "account-closed"
      //email = string account.Email
      //data = {| firstName = account.FirstName |}
      email = ""
      data = {| |}
     }
   // TODO: Include link to view statement
   | BillingStatement account -> {
      event = "billing-statement"
      //email = string account.Email
      //data = {| |}
      email = ""
      data = {| |}
     }
   | DebitDeclinedInsufficientAccountBalance(balance, email) -> {
      event = "debit-declined"
      email = string email
      data = {|
         reason =
            $"Your account has insufficient funds.  Your balance is ${balance}"
      |}
     }
   | DebitDeclinedExceededDailyDebit(limit, accrued, email) -> {
      event = "debit-declined"
      email = string email
      data = {|
         reason =
            $"You have spent ${accrued} today. 
              Your daily debit limit is set to ${limit}."
      |}
     }
   | TransferDeposited(evt, account) -> {
      event = "transfer-deposited"
      (*
      email = string account.Email
      data = {|
         firstName = account.FirstName
         amount = $"${evt.Data.Amount}"
         origin = evt.Data.Origin
      |}
     *)
      email = ""
      data = {| |}
     }
   | ApplicationErrorRequiresSupport errMsg -> {
      event = "application-error-requires-support"
      email = EnvNotifications.config.SupportEmail |> Option.defaultValue null
      data = {| error = errMsg |}
     }

// Side effect: Raise an exception instead of returning Result.Error
//              to trip circuit breaker
let private sendEmail
   (client: HttpClient)
   (data: TrackingEvent)
   (onError: string -> unit)
   =
   task {
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
         let msg = $"Error sending email: {response.ReasonPhrase} - {content}"
         onError msg
         failwith msg

      return response
   }

let private createClient (bearerToken: string) =
   let client =
      new HttpClient(BaseAddress = Uri(EnvNotifications.config.EmailServiceUri))

   client.DefaultRequestHeaders.Authorization <-
      Headers.AuthenticationHeaderValue("Bearer", bearerToken)

   client

let initQueueSource (throttle: StreamThrottle) =
   Source.queue OverflowStrategy.Backpressure 1000
   |> Source.throttle
         ThrottleMode.Shaping
         throttle.Burst
         throttle.Count
         throttle.Duration

let actorProps
   (system: ActorSystem)
   (breaker: Akka.Pattern.CircuitBreaker)
   (throttle: StreamThrottle)
   =
   let client =
      EnvNotifications.config.EmailBearerToken |> Option.map createClient

   let rec init (ctx: Actor<obj>) = actor {
      let! msg = ctx.Receive()

      match msg with
      | LifecycleEvent e ->
         match e with
         | PreStart ->
            logInfo ctx "Prestart - Initialize EmailActor Queue Source"

            let queue =
               initQueueSource throttle
               |> Source.toMat
                     (Sink.forEach (fun msg -> ctx.Self <! msg))
                     Keep.left
               |> Graph.run (system.Materializer())

            return! processing ctx queue
         | _ -> return ignored ()
      | msg ->
         logError ctx $"Unknown msg {msg}"
         return unhandled ()
   }

   and processing (ctx: Actor<obj>) (queue: ISourceQueueWithComplete<obj>) = actor {
      let! msg = ctx.Receive()

      match msg with
      | :? EmailMessage as msg ->
         let! result = queueOffer<obj> queue <| QueueItem msg

         return
            match result with
            | Ok effect -> effect
            | Error errMsg -> failwith errMsg
      | :? QueueItem as msg ->
         let (QueueItem msg) = msg
         let emailData = emailPropsFromMessage msg

         if client.IsNone then
            logWarning ctx "EmailBearerToken not set.  Will not send email."
         elif
            emailData.event = "application-error-requires-support"
            && isNull emailData.email
         then
            logWarning ctx "Support email not configured. Will not send."
         elif breaker.IsOpen then
            ctx.Stash()
         else
            let onSendFail _ =
               ctx.Schedule (TimeSpan.FromSeconds 15.) ctx.Self msg |> ignore

            breaker.WithCircuitBreaker(fun () ->
               sendEmail client.Value emailData onSendFail)
            |> Async.AwaitTask
            |!> retype ctx.Self
      | LifecycleEvent e ->
         match e with
         | PostStop ->
            queue.Complete()
            return! init ctx
         | _ -> return ignored ()
      | :? HttpResponseMessage ->
         // Successful request to email service -> ignore
         return ignored ()
      | :? Status.Failure as e ->
         logError ctx $"Failed request to email service {e}"
         return ignored ()
      | :? CircuitBreakerMessage as msg ->
         match msg with
         | BreakerHalfOpen ->
            logInfo ctx "Breaker half open - unstash one"
            ctx.Unstash()
            return ignored ()
         | BreakerClosed ->
            logInfo ctx "Breaker closed - unstash all"
            ctx.UnstashAll()
            return ignored ()
      | msg ->
         logError ctx $"Unknown msg {msg}"
         return unhandled ()
   }

   props init

let start
   (system: ActorSystem)
   (broadcaster: AccountBroadcast)
   (throttle: StreamThrottle)
   (breaker: Akka.Pattern.CircuitBreaker)
   : IActorRef<EmailMessage>
   =
   let ref =
      spawn system ActorMetadata.email.Name
      <| actorProps system breaker throttle

   breaker.OnHalfOpen(fun () ->
      broadcaster.circuitBreaker {
         Service = CircuitBreakerService.Email
         Status = CircuitBreakerStatus.HalfOpen
         Timestamp = DateTime.UtcNow
      }
      |> ignore

      ref <! BreakerHalfOpen)
   |> ignore

   breaker.OnClose(fun () ->
      broadcaster.circuitBreaker {
         Service = CircuitBreakerService.Email
         Status = CircuitBreakerStatus.Closed
         Timestamp = DateTime.UtcNow
      }
      |> ignore

      ref <! BreakerClosed)
   |> ignore

   breaker.OnOpen(fun () ->
      SystemLog.warning system "Email circuit breaker open"

      broadcaster.circuitBreaker {
         Service = CircuitBreakerService.Email
         Status = CircuitBreakerStatus.Open
         Timestamp = DateTime.UtcNow
      }
      |> ignore)
   |> ignore

   retype ref

let get (system: ActorSystem) : IActorRef<EmailMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.EmailMarker>()

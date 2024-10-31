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
open System.Threading.Tasks
open FsToolkit.ErrorHandling

open Lib.Postgres
open Lib.Types
open Lib.SharedTypes
open ActorUtil
open Bank.Account.Domain
open Bank.Employee.Domain

type EmployeeInviteEmailInfo = {
   Name: string
   Email: Email
   Token: InviteToken
   OrgId: OrgId
}

type TransferDepositEmailInfo = {
   AccountName: string
   Amount: decimal
   SenderBusinessName: string
   OrgId: OrgId
}

type PurchaseDeclinedEmailInfo = {
   Email: Email
   Reason: PurchaseDeclinedReason
   OrgId: OrgId
}

[<RequireQualifiedAccess>]
type EmailMessage =
   | AccountOpen of accountName: string * OrgId
   | AccountClose of accountName: string * OrgId
   | BillingStatement of accountName: string * OrgId
   | PurchaseDeclined of PurchaseDeclinedEmailInfo
   | InternalTransferBetweenOrgsDeposited of TransferDepositEmailInfo
   | ApplicationErrorRequiresSupport of error: string * OrgId
   | EmployeeInvite of EmployeeInviteEmailInfo

type private CircuitBreakerMessage =
   | BreakerHalfOpen
   | BreakerClosed

type private QueueItem = QueueItem of EmailMessage

module private TrackingEvent =
   type PreliminaryT = {
      OrgId: OrgId
      Event: string
      Email: string option
      Data: obj
   }

   type T = {
      OrgId: string
      Event: string
      Email: string
      Data: obj
   }

   let create (o: PreliminaryT) : T option =
      o.Email
      |> Option.map (fun email -> {
         OrgId = string o.OrgId
         Event = o.Event
         Email = email
         Data = o.Data
      })

let private emailPropsFromMessage
   (msg: EmailMessage)
   : TrackingEvent.PreliminaryT
   =
   match msg with
   | EmailMessage.AccountOpen(accountName, orgId) -> {
      OrgId = orgId
      Event = "account-opened"
      Email = None
      Data = {| name = accountName |}
     }
   | EmailMessage.AccountClose(accountName, orgId) -> {
      OrgId = orgId
      Event = "account-closed"
      Email = None
      Data = {| name = accountName |}
     }
   // TODO: Include link to view statement
   | EmailMessage.BillingStatement(accountName, orgId) -> {
      OrgId = orgId
      Event = "billing-statement"
      Email = None
      Data = {| name = accountName |}
     }
   | EmailMessage.PurchaseDeclined info -> {
      OrgId = info.OrgId
      Event = "debit-declined"
      Email = Some(string info.Email)
      Data = {|
         reason = PurchaseDeclinedReason.display info.Reason
      |}
     }
   | EmailMessage.InternalTransferBetweenOrgsDeposited info -> {
      OrgId = info.OrgId
      Event = "transfer-deposited"
      Email = None
      Data = {|
         name = info.AccountName
         amount = $"${info.Amount}"
         origin = info.SenderBusinessName
      |}
     }
   | EmailMessage.ApplicationErrorRequiresSupport(errMsg, orgId) -> {
      OrgId = orgId
      Event = "application-error-requires-support"
      Email = EnvNotifications.config.SupportEmail
      Data = {| error = errMsg |}
     }
   | EmailMessage.EmployeeInvite info -> {
      OrgId = info.OrgId
      Event = "employee-invite"
      Email = Some(string info.Email)
      Data = {|
         name = info.Name
         // TODO: Domain not configured.
         inviteLink =
            $"localhost:8080{RoutePaths.UserSessionPath.AuthorizeInvite}?token={info.Token.Token}"
      |}
     }

// NOTE
// Raise an exception instead of returning Result.Error to trip circuit breaker.
let private sendEmail
   (client: HttpClient)
   (data: TrackingEvent.T)
   (onError: string -> unit)
   =
   task {
      use! response =
         client.PostAsJsonAsync(
            "track",
            {|
               orgId = data.OrgId
               event = data.Event
               email = data.Email
               data = data.Data
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
   (getAdminEmailsForOrg: OrgId -> Task<Result<Email list option, Err>>)
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

      let sendEmailWithCircuitBreaker emailData =
         let onSendFail _ =
            ctx.Schedule (TimeSpan.FromSeconds 15.) ctx.Self msg |> ignore

         breaker.WithCircuitBreaker(fun () ->
            sendEmail client.Value emailData onSendFail)
         |> Async.AwaitTask
         |!> retype ctx.Self

      match msg with
      | :? EmailMessage as msg ->
         let! result = queueOffer<obj> queue <| QueueItem msg

         return
            match result with
            | Ok effect -> effect
            | Error errMsg -> failwith errMsg
      | :? QueueItem as msg ->
         let (QueueItem msg) = msg

         if client.IsNone then
            logWarning ctx "EmailBearerToken not set.  Will not send email."
         elif breaker.IsOpen then
            ctx.Stash()
         else
            // TODO:
            // Maybe create a configurable notification api
            // later if I get bored.  For now just send emails
            // which aren't designated to a particular employee
            // to all admins of an organization.
            let preliminaryData = emailPropsFromMessage msg

            match TrackingEvent.create preliminaryData with
            | None ->
               let! emailsToSendToAdmins =
                  getAdminEmailsForOrg preliminaryData.OrgId
                  |> Async.AwaitTask
                  |> AsyncResultOption.map (
                     List.choose (fun email ->
                        TrackingEvent.create {
                           preliminaryData with
                              Email = Some(string email)
                        })
                  )

               match emailsToSendToAdmins with
               | Error err -> logError ctx $"Error getting admin emails - {err}"
               | Ok None ->
                  logError
                     ctx
                     $"Could not retrieve admin emails for email message: {msg}"
               | Ok(Some emails) ->
                  emails |> List.iter sendEmailWithCircuitBreaker
            | Some info -> sendEmailWithCircuitBreaker info
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

module Fields = EmployeeSqlMapper.EmployeeFields
module Reader = EmployeeSqlMapper.EmployeeSqlReader
module Writer = EmployeeSqlMapper.EmployeeSqlWriter

let getAdminEmailsForOrg (orgId: OrgId) =
   let roleTypecast = EmployeeSqlMapper.EmployeeTypeCast.role
   let statusTypecast = EmployeeSqlMapper.EmployeeTypeCast.status

   pgQuery<Email>
      $"""
      SELECT {Fields.email} FROM {EmployeeSqlMapper.table}
      WHERE
         {Fields.orgId} = @orgId
         AND {Fields.role} = @role::{roleTypecast}
         AND {Fields.status} = @status::{statusTypecast}
      """
      (Some [
         "orgId", Writer.orgId orgId
         "role", Writer.role Role.Admin
         "status", Writer.status EmployeeStatus.Active
      ])
      Reader.email

let start
   (system: ActorSystem)
   (broadcaster: AccountBroadcast)
   (throttle: StreamThrottle)
   (breaker: Akka.Pattern.CircuitBreaker)
   : IActorRef<EmailMessage>
   =
   let ref =
      spawn system ActorMetadata.email.Name
      <| actorProps system breaker throttle getAdminEmailsForOrg

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

let getForwarder (system: ActorSystem) : IActorRef<EmailMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.EmailForwardingMarker>()

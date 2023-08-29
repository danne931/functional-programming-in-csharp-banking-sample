[<RequireQualifiedAccess>]
module EmailActor

open Akka.Actor
open Akka.Hosting
open Akkling
open System
open System.Net.Http
open System.Net.Http.Json

open BankTypes
open Lib.ActivePatterns
open Lib.Types
open ActorUtil
open Bank.Transfer.Domain

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

   if response.IsSuccessStatusCode then
      return Ok response
   else
      let! content = response.Content.ReadFromJsonAsync()
      return Error $"Error sending email: {response.ReasonPhrase} - {content}"
}

let private createClient (bearerToken: string) =
   let client =
      new HttpClient(BaseAddress = Uri("https://api.useplunk.com/v1/"))

   client.DefaultRequestHeaders.Authorization <-
      Headers.AuthenticationHeaderValue("Bearer", bearerToken)

   client

let start (system: ActorSystem) =
   let emailBearerToken = Environment.GetEnvironmentVariable("EmailBearerToken")

   let client =
      match isNull emailBearerToken with
      | true -> None
      | false -> Some(createClient emailBearerToken)

   let handler (ctx: Actor<_>) (msg: obj) =
      match msg with
      | :? Result<HttpResponseMessage, string> as msg ->
         match msg with
         | Error errMsg ->
            logError ctx errMsg
            unhandled ()
         | Ok _ -> ignored ()
      | :? EmailMessage as msg ->
         let emailData =
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

         if client.IsNone then
            logWarning ctx "EmailBearerToken not set.  Will not send email."
         else
            sendEmail client.Value emailData |> Async.AwaitTask
            |!> retype ctx.Self

         ignored ()
      | msg ->
         logError ctx $"Unknown msg {msg}"
         unhandled ()

   spawn system ActorMetadata.email.Name (props <| actorOf2 handler)

let get (system: ActorSystem) : IActorRef<EmailMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.EmailMarker>()

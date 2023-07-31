module Bank.Transfer.Routes

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open System
open System.Threading.Tasks
open Akkling

open Bank.Transfer.Domain
open Bank.Account.Api
open ActorUtil

module private Path =
   let Base = "/transfers"
   let TransferRecipient = Base + "/register-recipient"

let startTransferRoutes (app: WebApplication) =
   app.MapPost(
      Path.TransferRecipient,
      Func<AccountActorFac, RegisterTransferRecipientCommand, Task<IResult>>(
         (fun fac command ->
            processCommand
               fac
               (Validators.registerTransferRecipient ())
               command
            |> RouteUtil.unwrapTaskValidation)
      )
   )
   |> ignore

   app.MapPost(
      Path.Base,
      Func<IActorRef<DomesticTransferRecipientActor.Message>, AccountActorFac, TransferCommand, Task<IResult>>
         (fun _ fac command ->
            processCommand fac (Validators.transfer ()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore
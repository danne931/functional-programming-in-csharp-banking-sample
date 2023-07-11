module Bank.Transfer.Routes

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open System
open Akkling

open BankTypes
open Bank.Transfer.Domain
open Bank.Account.Api

module private Path =
   let Base = "/transfers"
   let TransferRecipient = Base + "/register-recipient"

let startTransferRoutes (app: WebApplication) =
   app.MapPost(
      Path.TransferRecipient,
      Func<IActorRef<AccountCoordinatorMessage>, RegisterTransferRecipientCommand, IResult>(
         (fun coordinator command ->
            processCommand
               coordinator
               (Validators.registerTransferRecipient ())
               command
            |> RouteUtil.unwrapValidation)
      )
   )
   |> ignore

   app.MapPost(
      Path.Base,
      Func<IActorRef<AccountCoordinatorMessage>, TransferCommand, IResult>
         (fun coordinator command ->
            processCommand coordinator (Validators.transfer ()) command
            |> RouteUtil.unwrapValidation)
   )
   |> ignore

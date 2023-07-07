module Bank.Transfer.Routes

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open System
open System.Threading.Tasks
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
      Func<IActorRef<AccountCoordinatorMessage>, RegisterTransferRecipientCommand, Task<IResult>>(
         (fun coordinator command ->
            processCommand
               coordinator
               (Validators.registerTransferRecipient ())
               command
            |> RouteUtil.UnwrapValidation)
      )
   )
   |> ignore

   app.MapPost(
      Path.Base,
      Func<IActorRef<AccountCoordinatorMessage>, TransferCommand, Task<IResult>>
         (fun coordinator command ->
            processCommand coordinator (Validators.transfer ()) command
            |> RouteUtil.UnwrapValidation)
   )
   |> ignore

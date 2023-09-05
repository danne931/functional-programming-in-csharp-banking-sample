module Bank.Transfer.Routes

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open System
open System.Threading.Tasks

open Bank.Transfer.Domain
open Bank.Account.Api
open ActorUtil

module private Path =
   let Base = "/transfers"
   let TransferRecipient = Base + "/register-recipient"

let startTransferRoutes (app: WebApplication) =
   app.MapPost(
      Path.TransferRecipient,
      Func<AccountActorFac, RegisterTransferRecipientCommand, Task<IResult>>
         (fun fac cmd ->
            match cmd.Recipient.AccountEnvironment with
            | RecipientAccountEnvironment.Internal ->
               TransferRecipientEvent.local cmd
               |> processCommand fac cmd
               |> RouteUtil.unwrapTaskResult
            | RecipientAccountEnvironment.Domestic ->
               TransferRecipientEvent.domestic cmd
               |> processCommand fac cmd
               |> RouteUtil.unwrapTaskResult
            | RecipientAccountEnvironment.International ->
               TransferRecipientEvent.domestic cmd
               |> processCommand fac cmd
               |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.Base,
      Func<AccountActorFac, TransferCommand, Task<IResult>>(fun fac cmd ->
         cmd.toEvent () |> processCommand fac cmd |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

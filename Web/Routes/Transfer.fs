module Bank.Transfer.Routes

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open System
open System.Threading.Tasks
open Akka.Actor

open Bank.Transfer.Domain
open Bank.Account.Api

module private Path =
   let Base = "/transfers"
   let TransferRecipient = Base + "/register-recipient"

let startTransferRoutes (app: WebApplication) =
   app.MapPost(
      Path.TransferRecipient,
      Func<ActorSystem, RegisterTransferRecipientCommand, Task<IResult>>
         (fun sys cmd ->
            match cmd.Recipient.AccountEnvironment with
            | RecipientAccountEnvironment.Internal ->
               TransferRecipientEvent.local cmd
               |> processCommand sys cmd
               |> RouteUtil.unwrapTaskResult
            | RecipientAccountEnvironment.Domestic ->
               TransferRecipientEvent.domestic cmd
               |> processCommand sys cmd
               |> RouteUtil.unwrapTaskResult
            | RecipientAccountEnvironment.International ->
               TransferRecipientEvent.domestic cmd
               |> processCommand sys cmd
               |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.Base,
      Func<ActorSystem, TransferCommand, Task<IResult>>(fun sys cmd ->
         cmd.toEvent () |> processCommand sys cmd |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

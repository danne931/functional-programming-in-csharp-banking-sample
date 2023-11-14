module Bank.Transfer.Routes

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open System
open System.Threading.Tasks
open Akka.Actor

open Bank.Transfer.Domain
open Bank.Account.Api
open Bank.Account.Domain

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
               |> processCommand
                     sys
                     (AccountCommand.RegisterTransferRecipient cmd)
                     cmd.EntityId
               |> RouteUtil.unwrapTaskResult
            | RecipientAccountEnvironment.Domestic ->
               TransferRecipientEvent.domestic cmd
               |> processCommand
                     sys
                     (AccountCommand.RegisterTransferRecipient cmd)
                     cmd.EntityId
               |> RouteUtil.unwrapTaskResult
            | RecipientAccountEnvironment.International ->
               TransferRecipientEvent.domestic cmd
               |> processCommand
                     sys
                     (AccountCommand.RegisterTransferRecipient cmd)
                     cmd.EntityId
               |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.Base,
      Func<ActorSystem, TransferCommand, Task<IResult>>(fun sys cmd ->
         processCommand
            sys
            (AccountCommand.Transfer cmd)
            cmd.EntityId
            (cmd.toEvent ())
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

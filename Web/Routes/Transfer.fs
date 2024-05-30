module Bank.Transfer.Routes

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open System
open System.Threading.Tasks
open Akka.Actor

open Lib.SharedTypes
open Bank.Transfer.Domain
open Bank.Account.Api
open Bank.Account.Domain
open RoutePaths

let startTransferRoutes (app: WebApplication) =
   app.MapPost(
      TransferPath.TransferRecipient,
      Func<ActorSystem, RegisterTransferRecipientCommand, Task<IResult>>
         (fun sys cmd ->
            match cmd.Data.Recipient.AccountEnvironment with
            | RecipientAccountEnvironment.Internal ->
               TransferRecipientEvent.local cmd
               |> processCommand
                     sys
                     (AccountCommand.RegisterTransferRecipient cmd)
                     (AccountId.fromEntityId cmd.EntityId)
               |> RouteUtil.unwrapTaskResult
            | RecipientAccountEnvironment.Domestic ->
               TransferRecipientEvent.domestic cmd
               |> processCommand
                     sys
                     (AccountCommand.RegisterTransferRecipient cmd)
                     (AccountId.fromEntityId cmd.EntityId)
               |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      TransferPath.Base,
      Func<ActorSystem, TransferCommand, Task<IResult>>(fun sys cmd ->
         processCommand
            sys
            (AccountCommand.Transfer cmd)
            (AccountId.fromEntityId cmd.EntityId)
            (TransferCommand.toEvent cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      TransferPath.NicknameRecipient,
      Func<ActorSystem, NicknameRecipientCommand, Task<IResult>>(fun sys cmd ->
         processCommand
            sys
            (AccountCommand.NicknameRecipient cmd)
            (AccountId.fromEntityId cmd.EntityId)
            (NicknameRecipientCommand.toEvent cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

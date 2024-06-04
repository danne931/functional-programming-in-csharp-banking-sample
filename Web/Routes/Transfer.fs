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
open RoutePaths

let startTransferRoutes (app: WebApplication) =
   app.MapPost(
      TransferPath.InternalTransferRecipient,
      Func<ActorSystem, RegisterInternalTransferRecipientCommand, Task<IResult>>
         (fun sys cmd ->
            processCommand
               sys
               (AccountCommand.RegisterInternalTransferRecipient cmd)
            |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      TransferPath.DomesticTransferRecipient,
      Func<ActorSystem, RegisterDomesticTransferRecipientCommand, Task<IResult>>
         (fun sys cmd ->
            processCommand
               sys
               (AccountCommand.RegisterDomesticTransferRecipient cmd)
            |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      TransferPath.Internal,
      Func<ActorSystem, InternalTransferCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (AccountCommand.InternalTransfer cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      TransferPath.Domestic,
      Func<ActorSystem, DomesticTransferCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (AccountCommand.DomesticTransfer cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      TransferPath.NicknameRecipient,
      Func<ActorSystem, NicknameRecipientCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (AccountCommand.NicknameRecipient cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

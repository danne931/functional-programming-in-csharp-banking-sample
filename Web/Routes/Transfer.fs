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
open Bank.UserSession.Middleware

let startTransferRoutes (app: WebApplication) =
   app
      .MapPost(
         TransferPath.DomesticTransferRecipient,
         Func<
            ActorSystem,
            RegisterDomesticTransferRecipientCommand,
            Task<IResult>
          >
            (fun sys cmd ->
               processCommand
                  sys
                  (AccountCommand.RegisterDomesticTransferRecipient cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManageTransferRecipient)
   |> ignore

   app
      .MapPost(
         TransferPath.DomesticTransferRecipientEdit,
         Func<ActorSystem, EditDomesticTransferRecipientCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand
                  sys
                  (AccountCommand.EditDomesticTransferRecipient cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManageTransferRecipient)
   |> ignore

   app
      .MapPost(
         TransferPath.InternalWithinOrg,
         Func<ActorSystem, InternalTransferWithinOrgCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (AccountCommand.InternalTransfer cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.SubmitTransfer)
   |> ignore


   app
      .MapPost(
         TransferPath.InternalCrossOrg,
         Func<ActorSystem, InternalTransferBetweenOrgsCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand
                  sys
                  (AccountCommand.InternalTransferBetweenOrgs cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.SubmitTransfer)
   |> ignore

   app
      .MapPost(
         TransferPath.Domestic,
         Func<ActorSystem, DomesticTransferCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (AccountCommand.DomesticTransfer cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.SubmitTransfer)
   |> ignore

   app
      .MapPost(
         TransferPath.NicknameRecipient,
         Func<ActorSystem, NicknameRecipientCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (AccountCommand.NicknameRecipient cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManageTransferRecipient)
   |> ignore

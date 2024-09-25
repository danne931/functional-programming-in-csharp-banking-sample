module Bank.Transfer.Routes

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open System
open System.Threading.Tasks
open Akka.Actor

open Lib.SharedTypes
open Bank.Transfer.Api
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
         TransferPath.InternalBetweenOrgs,
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
         TransferPath.ScheduleInternalBetweenOrgs,
         Func<
            ActorSystem,
            ScheduleInternalTransferBetweenOrgsCommand,
            Task<IResult>
          >
            (fun sys cmd ->
               processCommand
                  sys
                  (AccountCommand.ScheduleInternalTransferBetweenOrgs cmd)
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
         TransferPath.ScheduleDomestic,
         Func<ActorSystem, ScheduleDomesticTransferCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (AccountCommand.ScheduleDomesticTransfer cmd)
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

   app
      .MapGet(
         PaymentPath.Payments,
         Func<Guid, Task<IResult>>(fun orgId ->
            OrgId orgId |> getPayments |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.ViewPayments)
   |> ignore

   app
      .MapPost(
         PaymentPath.RequestPayment,
         Func<ActorSystem, RequestPlatformPaymentCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (AccountCommand.RequestPlatformPayment cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManagePayment)
   |> ignore

   app
      .MapPost(
         PaymentPath.CancelPayment,
         Func<ActorSystem, CancelPlatformPaymentCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (AccountCommand.CancelPlatformPayment cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManagePayment)
   |> ignore

   app
      .MapPost(
         PaymentPath.DeclinePayment,
         Func<ActorSystem, DeclinePlatformPaymentCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (AccountCommand.DeclinePlatformPayment cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManagePayment)
   |> ignore

   app
      .MapPost(
         PaymentPath.FulfillPayment,
         Func<ActorSystem, FulfillPlatformPaymentCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (AccountCommand.FulfillPlatformPayment cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManagePayment)
   |> ignore

   app
      .MapPost(
         TransferPath.ConfigureAutoTransferRule,
         Func<ActorSystem, ConfigureAutoTransferRuleCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand
                  sys
                  (AccountCommand.ConfigureAutoTransferRule cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManageAutoTransferRules)
   |> ignore

   app
      .MapPost(
         TransferPath.DeleteAutoTransferRule,
         Func<ActorSystem, DeleteAutoTransferRuleCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (AccountCommand.DeleteAutoTransferRule cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManageAutoTransferRules)
   |> ignore

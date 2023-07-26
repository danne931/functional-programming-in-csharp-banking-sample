[<RequireQualifiedAccess>]
module MaintenanceFeeActor

open System
open Akkling

open BankTypes
open Bank.Account.Domain
open ActorUtil

type private Message = | Start

let start (scheduledAt: _ -> TimeSpan) (mailbox: Actor<_>) (accountId: Guid) =
   let actorName = (ActorMetadata.maintenanceFee accountId).Name

   let handler (ctx: Actor<obj>) =
      let time = scheduledAt ()
      let schedule = ctx.ScheduleRepeatedly time time ctx.Self Start

      actor {
         let! msg = ctx.Receive()

         match msg with
         | LifecycleEvent e ->
            match e with
            | PostStop -> schedule.Cancel()
            | _ -> Ignore
         | :? Message ->
            let! account = ctx.Parent<AccountMessage>() <? Lookup

            let criteria = account.MaintenanceFeeCriteria

            if
               criteria.QualifyingDepositFound || criteria.DailyBalanceThreshold
            then
               let cmd = SkipMaintenanceFeeCommand(accountId, criteria)
               ctx.Parent<AccountMessage>() <! AccountMessage.StateChange cmd
            else
               let cmd = MaintenanceFeeCommand accountId
               ctx.Parent<AccountMessage>() <! AccountMessage.StateChange cmd
         | _ -> Unhandled
      }

   spawn mailbox actorName (props handler) |> ignore

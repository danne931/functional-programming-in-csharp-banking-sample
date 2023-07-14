[<RequireQualifiedAccess>]
module MaintenanceFeeActor

open System
open System.Threading.Tasks
open Microsoft.FSharp.Core.Option
open Akkling

open BankTypes
open Bank.Account.Domain
open MaintenanceFee
open ActorUtil

let private canIssueMaintenanceFee
   (getAccountEvents: Guid -> AccountEvent List Option Task)
   lookBackDate
   accountId
   =
   let eventsOpt = getAccountEvents(accountId).Result

   eventsOpt
   |> Option.bind (fun events ->
      let res = computeFeeCriteria lookBackDate events

      if res.depositCriteria || res.balanceCriteria then
         printfn
            "Some criteria met for skipping the monthly maintenance fee: Deposit (%A) / Balance (%A)"
            res.depositCriteria
            res.balanceCriteria

         None
      else
         Some res)

let start
   (getAccountEvents: Guid -> AccountEvent List Option Task)
   (lookBackDate: _ -> DateTime)
   (scheduledAt: _ -> TimeSpan)
   (mailbox: Actor<_>)
   (accountId: Guid)
   =
   let actorName = (ActorMetadata.maintenanceFee accountId).Name

   let handler (ctx: Actor<obj>) =
      let schedule =
         ctx.ScheduleRepeatedly
            (scheduledAt ())
            (scheduledAt ())
            ctx.Self
            accountId

      actor {
         let! msg = ctx.Receive()

         match msg with
         | LifecycleEvent e ->
            match e with
            | PostStop -> schedule.Cancel()
            | _ -> Ignore
         | :? Guid ->
            let criteriaMet =
               canIssueMaintenanceFee
                  getAccountEvents
                  (lookBackDate ())
                  accountId

            if isSome criteriaMet then
               let cmd = MaintenanceFeeCommand accountId
               ctx.Parent<AccountMessage>() <! AccountMessage.StateChange cmd
         | _ -> Unhandled
      }

   spawn mailbox actorName (props handler) |> ignore

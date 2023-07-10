module ActorUtil

open System
open Akkling
open Akka.Actor

let getActorRef actorCtx path = task {
   try
      let! aref = (select actorCtx path).ResolveOne(TimeSpan.FromSeconds 3)
      return Some aref
   with ex when true ->
      printfn "ActorNotFoundException: %A %A" path ex
      return None
}

let getChildActorRef<'t, 'r>
   (actorCtx: Actor<'t>)
   (path: string)
   : IActorRef<'r> option
   =
   let accountRef = actorCtx.UntypedContext.Child(path)

   match accountRef = ActorRefs.Nobody with
   | true -> None
   | false -> Some(typed accountRef)

module ActorMetadata =
   type ActorMetadata = { Name: string; Path: string }

   let accountCoordinator = {
      Name = "account_coordinator"
      Path = "akka://bank/user/account_coordinator"
   }

   let account (id: Guid) = {
      Name = string id
      Path = $"akka://bank/user/account_coordinator/{string id}"
   }

   let internalTransfer (id: Guid) = {
      Name = "internal_transfer_recipient"
      Path =
         $"akka://bank/user/account_coordinator/{string id}/internal_transfer_recipient"
   }

   let domesticTransfer = {
      Name = "domestic_transfer_recipient"
      Path = "akka://bank/user/domestic_transfer_recipient"
   }

   let maintenanceFee (id: Guid) = {
      Name = "maintenance_fee"
      Path = $"akka://bank/user/account_coordinator/{string id}/maintenance_fee"
   }

   let deadLettersMonitor = {
      Name = "dead_letters_monitor"
      Path = "akka://bank/user/dead_letters_monitor"
   }

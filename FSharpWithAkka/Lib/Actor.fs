module ActorUtil

open System
open Akkling

let getActorRef actorCtx path = task {
   try
      let! aref = (select actorCtx path).ResolveOne(TimeSpan.FromSeconds 3)
      return Some aref
   with ex when true ->
      printfn "ActorNotFoundException: %A %A" path ex
      return None
}

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

module AsyncUtil

open Fable.Core

type AsyncOperationStatus<'t> =
   | Started
   | Finished of 't

type Deferred<'t> =
   | Idle
   | InProgress
   | Resolved of 't

module Deferred =
   /// Returns whether the `Deferred<'T>` value has been resolved or not.
   let resolved =
      function
      | Idle -> false
      | InProgress -> false
      | Resolved _ -> true

   /// Returns whether the `Deferred<'T>` value is in progress or not.
   let inProgress =
      function
      | Idle -> false
      | InProgress -> true
      | Resolved _ -> false

   /// Transforms the underlying value of the input deferred value when it exists from type to another
   let map (transform: 'T -> 'U) (deferred: Deferred<'T>) : Deferred<'U> =
      match deferred with
      | Idle -> Idle
      | InProgress -> InProgress
      | Resolved value -> Resolved(transform value)

   /// Verifies that a `Deferred<'T>` value is resolved and the resolved data satisfies a given requirement.
   let exists (predicate: 'T -> bool) =
      function
      | Idle -> false
      | InProgress -> false
      | Resolved value -> predicate value

   /// Like `map` but instead of transforming just the value into another type in the `Resolved` case, it will transform the value into potentially a different case of the the `Deferred<'T>` type.
   let bind
      (transform: 'T -> Deferred<'U>)
      (deferred: Deferred<'T>)
      : Deferred<'U>
      =
      match deferred with
      | Idle -> Idle
      | InProgress -> InProgress
      | Resolved value -> transform value

let promiseToAsyncResult (promise: JS.Promise<'t>) : Async<Result<'t, exn>> = async {
   try
      let! result = Async.AwaitPromise promise
      return Ok result
   with err ->
      return Error err
}

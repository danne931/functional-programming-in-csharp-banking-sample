[<RequireQualifiedAccess>]
module Stub

open System
open Bank.Account.Domain

let billingStatement =
   BillingStatement.billingStatement
      AccountStub.accountStateAfterCreateWithEvents
      AccountStub.billingPeriod
      Int64.MaxValue

let accountBroadcast: AccountBroadcast = {
   accountEventPersisted = fun evt accountState -> ()
   accountEventValidationFail = fun accountId msg -> ()
   accountEventPersistenceFail = fun accountId msg -> ()
   circuitBreaker = fun msg -> ()
}

let akkaStreamsRestartSettings () =
   Akka.Streams.RestartSettings.Create(
      (TimeSpan.FromSeconds 3),
      (TimeSpan.FromSeconds 30),
      0.2
   )

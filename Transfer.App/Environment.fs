[<RequireQualifiedAccess>]
module EnvTransfer

open System
open System.Net
open FsConfig

open Lib.Types

let builder = Env.builder

type DomesticTransferRouter = { MaxInstancesPerNode: int }

type MockThirdPartyBank = { Host: IPAddress; Port: int }

// NOTE: These settings apply to all DomesticTransfer actors created
//       by the round robin pool router on a given account node.  There are
//       multiple account nodes each with their own CircuitBreaker instance.
//       The circuit breaker is coordinated across actors on a given node but
//       without adding more logic via Akka DistributedPubSub to keep the
//       circuit breaker instances in sync you should not expect the
//       circuit breaker mechanism to be coordinated across nodes.
//
//       Scenario with MaxFailures = 2
//       1. Transfer request on Node A -> Failure (BreakerClosed)
//       2. Transfer request on Node A -> Failure (BreakerOpen)
//       3. Transfer request on Node A -> Reattempt request when BreakerHalfOpen/BreakerClosed
//       4. Transfer request on Node B -> Failure (BreakerClosed)
//          - You would expect BreakerOpen at step 4 if the circuit breaker instances
//            were coordinated across nodes.
//       5. Transfer request on Node B -> Failure (BreakerOpen)
type private TransferConfigInput = {
   MockThirdPartyBank: {| Host: string option; Port: int |}
   DomesticTransferRouter: {| MaxInstancesPerNode: int option |}
   DomesticTransferCircuitBreaker: {|
      MaxFailures: int option
      CallTimeoutSeconds: int option
      ResetTimeoutSeconds: int option
   |}
   TransferProgressTrackingThrottle: Env.StreamThrottleInput
   TransferProgressLookbackMinutes: int option
}

type TransferConfig = {
   MockThirdPartyBank: MockThirdPartyBank
   DomesticTransferRouter: DomesticTransferRouter
   domesticTransferCircuitBreaker:
      Akka.Actor.ActorSystem -> Akka.Pattern.CircuitBreaker
   TransferProgressTrackingThrottle: StreamThrottle
   TransferProgressLookbackMinutes: int
}

let private getMockThirdPartyBankHost (host: string option) =
   match host with
   | Some ip -> IPAddress.Parse ip
   | None ->
      try
         // Referencing container by name to resolve IP for
         // mock third party bank server.
         Dns.GetHostAddresses("mock-third-party-bank")[0]
      with _ ->
         if not Env.isDev then
            failwith
               """
               IP for mock third party bank doesn't exist.
               Misconfigured container name.
               """
         else
            printfn "Configuring localhost for domestic transfer."
            IPAddress.Loopback

let config =
   match AppConfig(builder.Configuration).Get<TransferConfigInput>() with
   | Ok input -> {
      MockThirdPartyBank = {
         Host = getMockThirdPartyBankHost input.MockThirdPartyBank.Host
         Port = input.MockThirdPartyBank.Port
      }
      DomesticTransferRouter = {
         MaxInstancesPerNode =
            Option.defaultValue
               10
               input.DomesticTransferRouter.MaxInstancesPerNode
      }
      domesticTransferCircuitBreaker =
         fun system ->
            Akka.Pattern.CircuitBreaker(
               system.Scheduler,
               input.DomesticTransferCircuitBreaker.MaxFailures
               |> Option.defaultValue 2,

               input.DomesticTransferCircuitBreaker.CallTimeoutSeconds
               |> Option.defaultValue 7
               |> TimeSpan.FromSeconds,

               input.DomesticTransferCircuitBreaker.ResetTimeoutSeconds
               |> Option.defaultValue 20
               |> TimeSpan.FromSeconds
            )
      TransferProgressLookbackMinutes =
         input.TransferProgressLookbackMinutes
         |> Option.defaultValue (if Env.isProd then 60 else 0)
      TransferProgressTrackingThrottle = {
         Count =
            input.TransferProgressTrackingThrottle.Count
            |> Option.defaultValue 100
         Burst =
            input.TransferProgressTrackingThrottle.Burst
            |> Option.defaultValue 100
         Duration =
            input.TransferProgressTrackingThrottle.Seconds
            |> Option.defaultValue 10
            |> TimeSpan.FromSeconds
      }
     }
   | Error err ->
      match err with
      | NotFound key -> invalidArg key "Not found"
      | BadValue(key, value) -> invalidArg key $"{value} is invalid type for"
      | NotSupported key -> invalidArg key "Not supported"

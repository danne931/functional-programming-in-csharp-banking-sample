[<RequireQualifiedAccess>]
module EnvTransfer

open System.Net
open FsConfig

let builder = Env.builder

type private TransferConfigInput = {
   MockThirdPartyBank: {| Host: string option; Port: int |}
}

type MockThirdPartyBank = { Host: IPAddress; Port: int }

type TransferConfig = {
   MockThirdPartyBank: MockThirdPartyBank
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
     }
   | Error err ->
      match err with
      | NotFound key -> invalidArg key "Not found"
      | BadValue(key, value) -> invalidArg key $"{value} is invalid type for"
      | NotSupported key -> invalidArg key "Not supported"

[<RequireQualifiedAccess>]
module EnvScheduler

open FsConfig

let builder = Env.builder

type Quartz = {
   SchedulerName: string
   TablePrefix: string
}

type SchedulerConfig = { Quartz: Quartz }

let config =
   match AppConfig(builder.Configuration).Get<SchedulerConfig>() with
   | Ok input -> input
   | Error err ->
      match err with
      | NotFound key -> invalidArg key "Not found"
      | BadValue(key, value) -> invalidArg key $"{value} is invalid type for"
      | NotSupported key -> invalidArg key "Not supported"

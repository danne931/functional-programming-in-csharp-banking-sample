[<RequireQualifiedAccess>]
module Env

open Fable.Core

[<Emit("process.env[$0] ? process.env[$0] : ''")>]
let get (key: string) : string = jsNative

let environment = get "NODE_ENV"
let isDev = environment = "development"
let isProd = environment = "production"

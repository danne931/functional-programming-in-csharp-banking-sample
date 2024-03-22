[<RequireQualifiedAccess>]
module Log

let info (msg: string) =
   if Env.isDev then
      Browser.Dom.console.log msg

let error (errMsg: string) =
   if Env.isDev then
      Browser.Dom.console.error errMsg

[<RequireQualifiedAccess>]
module EnvNotifications

open FsConfig

let builder = Env.builder

// NOTE:
// For local development, EmailBearerToken & SupportEmail are set in ~/.microsoft/usersecrets.
// See https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-7.0&tabs=linux#set-a-secret

type NotificationsConfig = {
   EmailServiceUri: string
   EmailBearerToken: string option
   SupportEmail: string option
}

let private errorMessage missing =
   $"""
   ========================================================

     Missing {missing} configuration setting(s)

   ========================================================
   """

let config =
   match AppConfig(builder.Configuration).Get<NotificationsConfig>() with
   | Ok input ->
      let missing =
         [
            "EmailBearerToken", Option.isNone input.EmailBearerToken
            "SupportEmail", Option.isNone input.SupportEmail
         ]
         |> List.fold
            (fun acc (key, isNone) -> if isNone then key :: acc else acc)
            []

      if not missing.IsEmpty then
         let errMsg = errorMessage missing

         if not Env.isDev then
            // Fail if running app outside local dev environment.
            failwith errMsg
         else
            printfn "%A" errMsg

      {
         EmailServiceUri = input.EmailServiceUri
         EmailBearerToken = input.EmailBearerToken
         SupportEmail = input.SupportEmail
      }
   | Error err ->
      match err with
      | NotFound key -> invalidArg key "Not found"
      | BadValue(key, value) -> invalidArg key $"{value} is invalid type for"
      | NotSupported key -> invalidArg key "Not supported"

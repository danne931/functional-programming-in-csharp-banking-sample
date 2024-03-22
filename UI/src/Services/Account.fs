[<RequireQualifiedAccess>]
module AccountService

open Fable.SimpleHttp
open System
open FsToolkit.ErrorHandling

open Bank.Account.UIDomain
open Bank.Account.Domain
open Lib.SharedTypes
open RoutePaths

type ProcessingEventId = Guid

let private notImplemented (cmd: AccountCommand) =
   let msg = $"Not implemented command: {cmd}"
   Log.error msg
   failwith msg

let private commandToUrl cmd =
   match cmd with
   | AccountCommand.Debit _ -> AccountPath.Debit
   | AccountCommand.DepositCash _ -> AccountPath.Deposit
   | AccountCommand.Transfer _ -> TransferPath.Base
   | AccountCommand.RegisterTransferRecipient _ ->
      TransferPath.TransferRecipient
   | AccountCommand.LimitDailyDebits _ -> AccountPath.DailyDebitLimit
   | AccountCommand.LockCard _ -> AccountPath.LockCard
   | AccountCommand.UnlockCard _ -> AccountPath.UnlockCard
   | other -> notImplemented other

let postJson (url: string) (command: AccountCommand) =
   let serialized =
      match command with
      | AccountCommand.Debit cmd -> Serialization.serialize cmd
      | AccountCommand.DepositCash cmd -> Serialization.serialize cmd
      | AccountCommand.Transfer cmd -> Serialization.serialize cmd
      | AccountCommand.RegisterTransferRecipient cmd ->
         Serialization.serialize cmd
      | AccountCommand.LimitDailyDebits cmd -> Serialization.serialize cmd
      | AccountCommand.LockCard cmd -> Serialization.serialize cmd
      | AccountCommand.UnlockCard cmd -> Serialization.serialize cmd
      | other -> notImplemented other

   Http.request (url)
   |> Http.method HttpMethod.POST
   |> Http.content (BodyContent.Text serialized)
   |> Http.header (Headers.contentType "application/json")
   |> Http.send

let getAccounts () : Async<AccountsMaybe> = async {
   let! (code, responseText) = Http.get AccountPath.Base

   if code = 404 then
      return Ok None
   elif code <> 200 then
      return Error <| Err.InvalidStatusCodeError("AccountService", code)
   else
      return
         responseText
         |> Serialization.deserialize<AccountState list>
         |> Result.map (fun accounts ->
            [ for account in accounts -> account.EntityId, account ]
            |> Map.ofList
            |> Some)
}

let getAccount (id: Guid) : Async<AccountMaybe> = async {
   let! (code, responseText) = Http.get (AccountPath.account id)

   if code = 404 then
      return Ok None
   elif code <> 200 then
      return Error <| Err.InvalidStatusCodeError("AccountService", code)
   else
      return
         responseText
         |> Serialization.deserialize<AccountState>
         |> Result.map Some
}

let submitCommand
   (account: AccountState)
   (command: AccountCommand)
   : Async<Result<ProcessingEventId, Err>>
   =
   asyncResult {
      // Pre-network request validation checks the command itself
      // (Err.ValidationError) and the Result of applying the command
      // to the current state (Err.StateTransitionError).
      // This same validation occurs on the server when an actor is
      // processing a command.
      let! _ = Account.stateTransition account command

      let url = commandToUrl command
      let! res = postJson url command
      let code = res.statusCode

      if code <> 200 then
         return! Error <| Err.InvalidStatusCodeError("AccountService", code)
      else
         let! deserialized =
            Serialization.deserialize<Result<Guid, Err>> res.responseText

         return! deserialized
   }

let addAccountToSignalRConnectionGroup
   (connection: SignalR.Connection)
   (existingAccountId: Guid option)
   (accountIdToAdd: Guid)
   : Async<Result<Guid, Err>>
   =
   asyncResult {
      if
         existingAccountId.IsSome && existingAccountId <> Some accountIdToAdd
      then
         let! _ =
            connection.removeAccountFromConnectionGroup existingAccountId.Value

         ()

      let! _ = connection.addAccountToConnectionGroup accountIdToAdd

      return accountIdToAdd
   }

let listenForSignalRMessages
   (onAccountEventPersisted: AccountEventPersistedConfirmation -> unit)
   (conn: SignalR.Connection)
   =
   conn.on (
      "AccountEventPersistenceConfirmation",
      fun (msg: string) ->
         let deseri =
            Serialization.deserialize<AccountEventPersistedConfirmation> msg

         match deseri with
         | Error err -> Log.error (string err)
         | Ok msg -> onAccountEventPersisted msg
   )

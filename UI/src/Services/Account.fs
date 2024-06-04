[<RequireQualifiedAccess>]
module AccountService

open Fable.SimpleHttp
open FsToolkit.ErrorHandling
open Feliz.Router

open Bank.Account.UIDomain
open Bank.Account.Domain
open Lib.SharedTypes
open Lib.TransactionQuery
open RoutePaths

let private notImplemented (cmd: AccountCommand) =
   let msg = $"Account Service: Not implemented command: {cmd}"
   Log.error msg
   failwith msg

let postJson (command: AccountCommand) =
   let serialized, url =
      match command with
      | AccountCommand.Debit cmd ->
         Serialization.serialize cmd, AccountPath.Debit
      | AccountCommand.DepositCash cmd ->
         Serialization.serialize cmd, AccountPath.Deposit
      | AccountCommand.InternalTransfer cmd ->
         Serialization.serialize cmd, TransferPath.Internal
      | AccountCommand.DomesticTransfer cmd ->
         Serialization.serialize cmd, TransferPath.Domestic
      | AccountCommand.RegisterInternalTransferRecipient cmd ->
         Serialization.serialize cmd, TransferPath.InternalTransferRecipient
      | AccountCommand.RegisterDomesticTransferRecipient cmd ->
         Serialization.serialize cmd, TransferPath.DomesticTransferRecipient
      | AccountCommand.LimitDailyDebits cmd ->
         Serialization.serialize cmd, AccountPath.DailyDebitLimit
      | AccountCommand.LockCard cmd ->
         Serialization.serialize cmd, AccountPath.LockCard
      | AccountCommand.UnlockCard cmd ->
         Serialization.serialize cmd, AccountPath.UnlockCard
      | AccountCommand.NicknameRecipient cmd ->
         Serialization.serialize cmd, TransferPath.NicknameRecipient
      | other -> notImplemented other

   Http.postJson url serialized

let getAccountProfiles (orgId: OrgId) : Async<AccountProfilesMaybe> = async {
   let path =
      AccountPath.Base + Router.encodeQueryString [ "orgId", string orgId ]

   let! (code, responseText) = Http.get path

   if code = 404 then
      return Ok None
   elif code <> 200 then
      return Error <| Err.InvalidStatusCodeError("AccountService", code)
   else
      return
         responseText
         |> Serialization.deserialize<AccountProfile list>
         |> Result.map (fun accounts ->
            [ for account in accounts -> account.AccountId, account ]
            |> Map.ofList
            |> Some)
}

let getAccount (accountId: AccountId) : Async<Result<Account option, Err>> = async {
   let path = AccountPath.account accountId

   let! (code, responseText) = Http.get path

   if code = 404 then
      return Ok None
   elif code <> 200 then
      return Error <| Err.InvalidStatusCodeError("AccountService", code)
   else
      return
         responseText |> Serialization.deserialize<Account> |> Result.map Some
}

let getAccountAndTransactions
   (query: TransactionQuery)
   : Async<AccountAndTransactionsMaybe>
   =
   async {
      let basePath = AccountPath.accountAndTransactions query.AccountId

      let queryParams =
         query
         |> TransactionService.transactionQueryParams
         |> Router.encodeQueryString

      let path = basePath + queryParams

      let! (code, responseText) = Http.get path

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error <| Err.InvalidStatusCodeError("AccountService", code)
      else
         return
            responseText
            |> Serialization.deserialize<Account * AccountEvent list>
            |> Result.map Some
   }

let submitCommand
   (account: Account)
   (command: AccountCommand)
   : Async<Result<EventId, Err>>
   =
   asyncResult {
      // Pre-network request validation checks the command itself
      // (Err.ValidationError) and the Result of applying the command
      // to the current state (Err.StateTransitionError).
      // This same validation occurs on the server when an actor is
      // processing a command.
      let! _ = Account.stateTransition account command

      let! res = postJson command
      let code = res.statusCode

      if code <> 200 then
         return! Error <| Err.InvalidStatusCodeError("AccountService", code)
      else
         let! deserialized =
            Serialization.deserialize<Result<EventId, Err>> res.responseText

         return! deserialized
   }

let addAccountToSignalRConnectionGroup
   (connection: SignalR.Connection)
   (existingAccountId: AccountId option)
   (accountIdToAdd: AccountId)
   : Async<Result<AccountId, Err>>
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

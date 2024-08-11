[<RequireQualifiedAccess>]
module AccountService

open Fable.SimpleHttp
open FsToolkit.ErrorHandling
open Feliz.Router

open UIDomain.Account
open Bank.Account.Domain
open Lib.SharedTypes
open RoutePaths

let private notImplemented (cmd: AccountCommand) =
   let msg = $"Account Service: Not implemented command: {cmd}"
   Log.error msg
   failwith msg

let postJson (command: AccountCommand) =
   let serialized, url =
      match command with
      | AccountCommand.CreateAccount cmd ->
         Serialization.serialize cmd, AccountPath.Base
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
      | AccountCommand.EditDomesticTransferRecipient cmd ->
         Serialization.serialize cmd, TransferPath.DomesticTransferRecipientEdit
      | AccountCommand.NicknameRecipient cmd ->
         Serialization.serialize cmd, TransferPath.NicknameRecipient
      | other -> notImplemented other

   Http.postJson url serialized

let getOrgAndAccountProfiles
   (orgId: OrgId)
   : Async<Result<OrgWithAccountProfiles option, Err>>
   =
   async {
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
            |> Serialization.deserialize<OrgWithAccountProfiles>
            |> Result.map Some
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
   : Async<Result<AccountCommandReceipt, Err>>
   =
   asyncResult {
      // Pre-network request validation checks the command itself
      // (Err.ValidationError) and the Result of applying the command
      // to the current state (Err.StateTransitionError).
      // This same validation occurs on the server when an actor is
      // processing a command.
      let! evt, updatedState =
         Account.stateTransition { Info = account; Events = [] } command

      let! res = postJson command
      let code = res.statusCode

      if code <> 200 then
         return! Error <| Err.InvalidStatusCodeError("AccountService", code)
      else
         let! envelope = Serialization.deserialize<Envelope> res.responseText

         return {
            Envelope = envelope
            PendingState = updatedState.Info
            PendingEvent = evt
            PendingCommand = command
         }
   }

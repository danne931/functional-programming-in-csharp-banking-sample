module Bank.Account.Api

open System
open System.Threading.Tasks
open Microsoft.FSharp.Core.Option
open FSharp.Control
open Akkling

open BankTypes
open Bank.Account.Domain
open Lib.Types


let processCommand
   (accounts: IActorRef<AccountCoordinatorMessage>)
   (validate: Validator<'t>)
   command
   =
   let validation = validate command

   if Result.isOk validation then
      let msg = AccountCoordinatorMessage.StateChange command
      retype accounts <! msg.consistentHash ()

   Task.fromResult validation

let getAccountEvents id = Task.fromResult None

let getAccount
   (getAccountEvents: Guid -> AccountEvent list option Task)
   (accountId: Guid)
   =
   task {
      let! evtsOption = getAccountEvents accountId
      return map Account.foldEventsIntoAccount evtsOption
   }

let softDeleteEvents
   (accounts: IActorRef<AccountCoordinatorMessage>)
   accountId
   =
   let msg = AccountCoordinatorMessage.Delete accountId
   retype accounts <! msg.consistentHash ()
   Task.fromResult accountId

/// <summary>
/// Get all CreatedAccount events for UI demonstration purposes.
/// Allows demonstration consumer to choose what account to process
/// transactions on.
/// </summary>
let getAccountCreationEvents () = Task.fromResult None

let createAccount
   (accounts: IActorRef<AccountCoordinatorMessage>)
   (validate: Validator<CreateAccountCommand>)
   (command: CreateAccountCommand)
   =
   task {
      let validation = command |> validate |> Result.map (fun c -> c.EntityId)

      if Result.isError validation then
         return validation
      else
         let account = command |> CreatedAccountEvent.create |> Account.create

         let msg = AccountCoordinatorMessage.InitAccount account
         retype accounts <! msg.consistentHash ()

         return Ok account.EntityId
   }

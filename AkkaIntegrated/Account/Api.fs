module Bank.Account.Api

open System
open System.Threading.Tasks
open Microsoft.FSharp.Core.Option
open EventStore.Client
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

let getAccountEvents esClient id =
   EventStoreManager.readStream esClient (Account.streamName id) false

let getAccount
   (getAccountEvents: Guid -> AccountEvent list option Task)
   (accountId: Guid)
   =
   task {
      let! evtsOption = getAccountEvents accountId
      return map Account.foldEventsIntoAccount evtsOption
   }

let softDeleteEvents
   esClient
   (accounts: IActorRef<AccountCoordinatorMessage>)
   accountId
   =
   let msg = AccountCoordinatorMessage.Delete accountId
   retype accounts <! msg.consistentHash ()
   EventStoreManager.softDelete esClient (Account.streamName accountId)

/// <summary>
/// Get all CreatedAccount events for UI demonstration purposes.
/// Allows demonstration consumer to choose what account to process
/// transactions on.
/// </summary>
let getAccountCreationEvents esClient =
   EventStoreManager.readStream esClient "$et-CreatedAccount" true

let save esClient ((_, envelope) as props: OpenEventEnvelope) = task {
   do!
      EventStoreManager.save
         esClient
         (Account.streamName envelope.EntityId)
         props
         None
}

let createAccount
   esClient
   (accounts: IActorRef<AccountCoordinatorMessage>)
   (validate: Validator<CreateAccountCommand>)
   (command: CreateAccountCommand)
   =
   task {
      let validation = command |> validate |> Result.map (fun c -> c.EntityId)

      if Result.isError validation then
         return validation
      else
         let evt = CreatedAccountEvent.create command

         do!
            EventStoreManager.save
               esClient
               (Account.streamName evt.EntityId)
               (evt |> Envelope.wrap |> Envelope.unwrap)
               // Create event stream only if it doesn't already exist.
               (Some StreamState.NoStream)

         let acct = Account.create evt

         let msg = AccountCoordinatorMessage.InitAccount acct
         retype accounts <! msg.consistentHash ()

         return Ok evt.EntityId
   }

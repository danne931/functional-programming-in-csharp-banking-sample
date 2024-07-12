namespace Bank.Hubs

open Microsoft.AspNetCore.SignalR
open System.Threading.Tasks

type IAccountClient =
   abstract member AccountEventPersistenceConfirmation: string -> Task

   abstract member AccountEventPersistenceFail: string -> Task

   abstract member AccountEventValidationFail: string -> Task

   abstract member CircuitBreakerMessage: string -> Task

type AccountHub() =
   inherit Hub<IAccountClient>()

   member x.RemoveFromConnectionGroup(accountId: string) =
      x.Groups.RemoveFromGroupAsync(x.Context.ConnectionId, accountId)

   member x.AddToConnectionGroup(accountId: string) =
      x.Groups.AddToGroupAsync(x.Context.ConnectionId, accountId)

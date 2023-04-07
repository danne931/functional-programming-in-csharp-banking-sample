using Microsoft.AspNetCore.SignalR;

using Bank.Account.Domain;

namespace Bank.Hubs;

public record StateTransitionMessage(object Event, AccountState NewState);

public interface IAccountClient {
   Task ReceiveMessage(StateTransitionMessage stateTransition);
   Task ReceiveError(string error);
}

public class AccountHub : Hub<IAccountClient> {
   public async Task AddToConnectionGroup(string accountId) =>
      await Groups.AddToGroupAsync(Context.ConnectionId, accountId);
}
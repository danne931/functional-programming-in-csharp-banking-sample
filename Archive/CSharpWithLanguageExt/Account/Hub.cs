using Microsoft.AspNetCore.SignalR;

namespace Bank.Hubs;

public record StateTransitionMessage(object Event, object NewState);

public interface IAccountClient {
   Task ReceiveMessage(StateTransitionMessage stateTransition);
   Task ReceiveError(string error);
}

public class AccountHub : Hub<IAccountClient> {
   public async Task RemoveFromConnectionGroup(string accountId) =>
      await Groups.RemoveFromGroupAsync(Context.ConnectionId, accountId);

   public async Task AddToConnectionGroup(string accountId) =>
      await Groups.AddToGroupAsync(Context.ConnectionId, accountId);
}

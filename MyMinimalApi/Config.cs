using Echo;
using EventStore.Client;
using static LanguageExt.Prelude;
using Microsoft.AspNetCore.SignalR;

using ES = Lib.Persistence.EventStoreManager;
using Lib.Types;
using Bank.Transfer.Domain;
using static Bank.Transfer.Domain.Validators;
using Bank.Account.API;
using Bank.Account.Domain;
using Bank.Account.Actors;
using Bank.Hubs;

namespace Bank;

public static class Config {
   public static void StartActorModel() {
      ProcessConfig.initialise();
      Process.DeadLetters()
         .Observe<DeadLetter>()
         .Subscribe(Console.WriteLine);
   }

   public static EventStoreClient StartEventStore(WebApplicationBuilder builder)
      => ES.Connect(builder.Configuration.GetConnectionString("EventStore"));

   public static void InjectDependencies(
      WebApplicationBuilder builder,
      EventStoreClient esClient
   ) {
      builder.Services.AddSingleton<Validator<TransferCmd>>(TransferValidation());

      builder.Services.AddSingleton<AccountRegistry>(provider =>
         new AccountRegistry(
            loadAccount: id => AccountAPI.GetAccount(
               id => AccountAPI.GetAccountEvents(esClient, id),
               id
            ),
            saveAndPublish: evt => AccountAPI.SaveAndPublish(
               esClient,
               evt
            ),
            startChildActors: id => List(
               MaintenanceFeeActor.Start(
                  id => AccountAPI.GetAccountEvents(esClient, id),
                  //lookBackDate: () => DateTime.UtcNow.AddDays(-30),
                  //scheduledAt: () => TimeSpan.FromDays(30),
                  lookBackDate: () => DateTime.UtcNow.AddMinutes(-2),
                  scheduledAt: () => TimeSpan.FromMinutes(2),
                  id
               )
            ),
            broadcast: ((Event evt, AccountState state) stateTransition) =>
               provider
                  .GetRequiredService<IHubContext<AccountHub, IAccountClient>>()
                  .Clients.Group(stateTransition.Item2.EntityId.ToString())
                  .ReceiveMessage(
                     new StateTransitionMessage(
                        stateTransition.Item1,
                        // TEMPORARY fix LanguageExt.Map -> Dictionary
                        // Parser doesn't seem to recognize Map type
                        new {
                           TransferRecipients = stateTransition.Item2.TransferRecipients.ToDictionary(),
                           EntityId = stateTransition.Item2.EntityId,
                           FirstName = stateTransition.Item2.FirstName,
                           LastName = stateTransition.Item2.LastName,
                           Status = stateTransition.Item2.Status,
                           Balance = stateTransition.Item2.Balance,
                           DailyDebitLimit = stateTransition.Item2.DailyDebitLimit,
                           DailyDebitAccrued = stateTransition.Item2.DailyDebitAccrued
                        }
                     )
                  ),
            broadcastError: (string errMsg) =>
               provider
                  .GetRequiredService<IHubContext<AccountHub, IAccountClient>>()
                  .Clients.All.ReceiveError(errMsg)
         )
      );

      builder.Services.AddSingleton<EventStoreClient>(esClient);
   }
}
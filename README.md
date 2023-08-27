# Banking with Akka.NET

## Intent
Looking over this repo may be a useful next step after reading [Functional Programming in C#](https://www.manning.com/books/functional-programming-in-c-sharp-second-edition)
by **Enrico Buonanno** or material on F#, as well as material on Akka.  The [AkkaIntegrated directory](https://github.com/danne931/akka-dotnet-bank/tree/main/AkkaIntegrated) is a continuous effort aiming to utilize best practices in event sourcing and the actor model.  Integrating more tightly with Akka nudges us towards that end.  The AkkaIntegrated directory evolved from the FSharpWithAkka directory, which in turn evolved from the CSharpWithLanguageExt directory.  EventStoreDB, utilized in CSharpWithLanguageExt and FSharpWithAkka, is replaced with [Akka.Persistence](https://getakka.net/articles/persistence/architecture.html) via PostgreSQL.  Actor system initialization, manual dependency injection of ActorRef's and actor selection is replaced with Akka.Hosting and Akka's ActorRegistry.  We take advantage of [Akka.Cluster.Sharding](https://getakka.net/articles/clustering/cluster-sharding.html) for our account aggregate root.  Future/recurring actor message scheduling with PostgreSQL persistence is established via [Quartz.NET](https://www.quartz-scheduler.net/).

The [CSharpWithLanguageExt directory](https://github.com/danne931/akka-dotnet-bank/tree/main/CSharpWithLanguageExt) expands on **Enrico Buonanno**'s banking account example [actor](https://github.com/la-yumba/functional-csharp-code-2/blob/master/Examples/Chapter19/Boc/AccountProcess.cs)
and [domain logic](https://github.com/la-yumba/functional-csharp-code-2/blob/master/Examples/Chapter13/Domain/Account.cs) to include
additional business use cases as well as integration with more tech such as [EventStoreDB](https://www.eventstore.com/eventstoredb) and the
de facto library for functional programming in C#, [language-ext](https://github.com/louthy/language-ext).

The [FSharpWithAkka directory](https://github.com/danne931/akka-dotnet-bank/tree/main/FSharpWithAkka) is close to a one-to-one representation of the CSharpWithLanguageExt directory, with all use cases rewritten in F# and **Paul Louth**'s echo-process actor library replaced with [Akka](https://github.com/akkadotnet/akka.net).  [F#'s type inference](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/type-inference) and [computation expressions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions) makes writing programs with functional programming quite a bit more comfortable than with C#.

## Use Cases
1. Deposit
2. Debit
3. Registering a transfer recipient internal to the bank
4. Registering a transfer recipient in a 3rd party bank for domestic transfers (*exclusive to F# apps*)
5. Transferring money to a registered recipient (internal to the bank) debits the sender and credits the receiver
6. Transferring money to an account in a mock 3rd party bank demonstrates resilience in face of intermittent network issues.  Integration with Akka circuit breaker allows pending transfers to be reprocessed once the 3rd party bank is in a healthy state.  (*exclusive to F# apps*)
7. Recurring maintenance fee each billing cycle unless a qualified deposit found or a daily account balance threshold met.
8. Daily debit limit set by the customer
9. Lock/unlock debit card
10. Billing statements issued for each billing cycle (*exclusive to AkkaIntegrated app*)
11. Emails sent for account open/close, billing statement, debit declined, & transfer deposited (*exclusive to AkkaIntegrated app*)

![bank-8-26](https://github.com/danne931/akka-dotnet-bank/assets/4181901/24698fc1-c682-4ef9-8aee-23eefda45d38)

## UI
I created a [simple](https://github.com/danne931/akka-dotnet-bank/blob/main/AkkaIntegrated/wwwroot/js/account.js)
web page to test the use cases against an account.

[SignalR](https://dotnet.microsoft.com/en-us/apps/aspnet/signalr) is used to provide real-time feedback from actors to the UI:
- Overall account state
- History of transactions on the account
- Toggling between a transfer sender & receiver account (internal to the bank) demonstrates debits in one account and credits in the other
- A system operations navbar displays circuit breaker open/closed status for domestic transfers to the 3rd party bank mock server (*exclusive to F# apps*)
- When the circuit breaker closes, pending domestic transfers are reprocessed & corresponding Approved/Rejected events are interpolated into the table (*exclusive to F# apps*)

## Demonstration
### Domestic transfers to a mock 3rd party bank server with circuit breaker integration:
![bank-domestic-transfer](https://github.com/danne931/akka-dotnet-bank/assets/4181901/0c504ddd-8b56-4bcb-9001-107f4833e3d1)
### Transfers to accounts internal to the bank:
![bank-internal-transfer](https://github.com/danne931/akka-dotnet-bank/assets/4181901/fd71e49f-f08b-4af1-9a64-3bac96490d98)

## Running AkkaIntegrated
1. docker pull postgres
2. docker run --name bank-postgres-container -e POSTGRES_PASSWORD=password -d postgres -p 5432
3. run postgres container
4. if testing email notifications, you will need to create an account with [Plunk](https://app.useplunk.com/) and create events in Plunk such as [transfer-deposited](https://github.com/danne931/akka-dotnet-bank/blob/main/AkkaIntegrated/Notifications/EmailActor.fs#L70C14-L70C14).  You will also need to create templates and link events to templates via actions.  To authenticate you can add a [user secret](https://github.com/danne931/akka-dotnet-bank/blob/main/AkkaIntegrated/Lib/Config.fs#L342C39-L342C39) for the email bearer token.
5. navigate to AkkaIntegrated directory & dotnet run
6. navigate to MockThirdPartyBankTransferReceiver and dotnet run if testing domestic transfers
7. make POST requests to http://localhost:PORT/accounts to create accounts ex:
    ```
    {
	      "FirstName": "Jelly",
	      "LastName": "Fish",
	      "EntityId": "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a1",
	      "Balance": 931,
          "Email": "youremail@gmail.com",
	      "Currency": "USD"
    }
    ```
8. navigate to localhost in browser

## Running CSharpWithLanguageExt or FSharpWithAkka
1. docker pull eventstore/eventstore
2. docker run --name esdb-node -it -p 2113:2113 -p 1113:1113 eventstore/eventstore:latest --insecure --run-projections=All --mem-db
3. run eventstoredb container
4. enable eventstoredb projections: make an empty JSON POST request to http://127.0.0.1:2113/projection/$by_event_type/command/enable
5. navigate to the C# or FSharpWithAkka directories in this repo & dotnet run
6. navigate to MockThirdPartyBankTransferReceiver and dotnet run (*exclusive to F# apps*)
7. make POST requests to http://localhost:PORT/accounts to create accounts ex:
    ```
    {
	      "FirstName": "Jelly",
	      "LastName": "Fish",
	      "EntityId": "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a1",
	      "Balance": 931,
	      "Currency": "USD"
    }
    ```
8. navigate to localhost in browser
9. restart eventstoredb container if switching between running C# and F# apps

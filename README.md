# Banking with Akka.NET

## Intro
This project [(see AkkaIntegrated directory)](https://github.com/danne931/akka-dotnet-bank/tree/main/AkkaIntegrated) utilizes event sourcing and the actor model via Akka.NET to build typical banking functionality.  Event sourcing is implemented with [Akka.Persistence](https://getakka.net/articles/persistence/architecture.html) via PostgreSQL.  [Akka.Cluster.Sharding](https://getakka.net/articles/clustering/cluster-sharding.html) is utilized for the account aggregate root.  Future/recurring actor message scheduling with PostgreSQL persistence is established via [Quartz.NET](https://www.quartz-scheduler.net/).

## Use Cases
1. Deposit
2. Debit
3. Registering a transfer recipient internal to the bank
4. Registering a transfer recipient in a 3rd party bank for domestic transfers
5. Transferring money to a registered recipient (internal to the bank) debits the sender and credits the receiver
6. Transferring money to an account in a mock 3rd party bank demonstrates resilience in face of intermittent network issues.  Integration with Akka circuit breaker allows pending transfers to be reprocessed once the 3rd party bank is in a healthy state.
7. Recurring maintenance fee each billing cycle unless a qualified deposit found or a daily account balance threshold met.
8. Daily debit limit set by the customer
9. Lock/unlock debit card
10. Billing statements issued for each billing cycle
11. Emails sent for account open/close, billing statement, debit declined, & transfer deposited

![bank-9-1](https://github.com/danne931/akka-dotnet-bank/assets/4181901/b9d0a710-7ef6-43f2-8ac7-4580746f8853)

## UI
I created a [simple](https://github.com/danne931/akka-dotnet-bank/blob/main/AkkaIntegrated/wwwroot/js/account.js)
web page to test the use cases against an account.

[SignalR](https://dotnet.microsoft.com/en-us/apps/aspnet/signalr) is used to provide real-time feedback from actors to the UI:
- Overall account state
- History of transactions on the account
- Toggling between a transfer sender & receiver account (internal to the bank) demonstrates debits in one account and credits in the other
- A system operations navbar displays circuit breaker open/closed status for domestic transfers to the 3rd party bank mock server
- When the circuit breaker closes, pending domestic transfers are reprocessed & corresponding Approved/Rejected events are interpolated into the table

## Demonstration
### Domestic transfers to a mock 3rd party bank server with circuit breaker integration:
![bank-domestic-transfer](https://github.com/danne931/akka-dotnet-bank/assets/4181901/0c504ddd-8b56-4bcb-9001-107f4833e3d1)
### Transfers to accounts internal to the bank:
![bank-internal-transfer](https://github.com/danne931/akka-dotnet-bank/assets/4181901/fd71e49f-f08b-4af1-9a64-3bac96490d98)

## Running with Docker
1. docker compose up
2. navigate to localhost:3000
3. if you want to inspect postgres in a dashboard you can visit localhost:5008 (Server=postgres;Database=akkabank;Uid=postgres;Pwd=password)

## Running without Docker
1. Dependencies: .NET 7, PostgreSQL & the psql command-line interface
2. Create a database (Server=localhost;Database=akkabank;Uid=postgres;Pwd=password)
3. Seed the database: psql postgres < AkkaIntegrated/Migrations/*.sql
4. sh build.sh (builds & launches the app in watch mode)
5. navigate to MockThirdPartyBankTransferReceiver and dotnet run if testing domestic transfers

## Running tests
1. sh build.sh -t Test

## Archive
Inspiration for this project stemmed from reading [Functional Programming in C#](https://www.manning.com/books/functional-programming-in-c-sharp-second-edition)
by **Enrico Buonanno**.  

The first iteration of this project [(see CSharpWithLanguageExt directory)](https://github.com/danne931/akka-dotnet-bank/tree/main/Archive/CSharpWithLanguageExt) expands on **Enrico Buonanno**'s banking account example [actor](https://github.com/la-yumba/functional-csharp-code-2/blob/master/Examples/Chapter19/Boc/AccountProcess.cs)
and [domain logic](https://github.com/la-yumba/functional-csharp-code-2/blob/master/Examples/Chapter13/Domain/Account.cs) to include
additional business use cases as well as integration with more tech such as [EventStoreDB](https://www.eventstore.com/eventstoredb) and the
de facto library for functional programming in C#, [language-ext](https://github.com/louthy/language-ext).

The second iteration of this project [(see FSharpWithAkka directory)](https://github.com/danne931/akka-dotnet-bank/tree/main/Archive/FSharpWithAkka) is close to a one-to-one representation of the CSharpWithLanguageExt directory, with all use cases rewritten in F# and **Paul Louth**'s echo-process actor library replaced with Akka.  I saw that [F#'s type inference](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/type-inference) and [computation expressions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions) made writing programs with typed functional programming more second nature than with C# so I decided to continue with it for the third iteration (AkkaIntegrated directory) and beyond.

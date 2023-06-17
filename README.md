# Banking on dotnet functional programming

## Intent
I hope this repo can serve as a practical learning resource for incorporating functional programming concepts into your dotnet
apps following reading [Functional Programming in C#](https://www.manning.com/books/functional-programming-in-c-sharp-second-edition)
by **Enrico Buonanno**.  This repo expands on his banking account example [actor](https://github.com/la-yumba/functional-csharp-code-2/blob/master/Examples/Chapter19/Boc/AccountProcess.cs)
and [domain logic](https://github.com/la-yumba/functional-csharp-code-2/blob/master/Examples/Chapter13/Domain/Account.cs) to include
additional business use cases as well as integration with more tech such as [EventStoreDB](https://www.eventstore.com/eventstoredb) and the
de facto library for functional programming in C#, [language-ext](https://github.com/louthy/language-ext).

After looking over the project in the [C# directory](https://github.com/danne931/functional-programming-in-csharp-banking-sample/tree/main/CSharpWithLanguageExt)
I highly recommend checking out the [F# directory](https://github.com/danne931/functional-programming-in-csharp-banking-sample/tree/main/FSharpWithAkka)
where you'll find a rewrite of all use cases in F#.  [F#'s type inference](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/type-inference)
makes writing programs with functional programming quite a bit more comfortable than with C#.
If you've found your way around the C# directory then the F# directory structure should feel quite
similar.  Once there you will see we no longer need **Paul Louth**'s language-ext and his echo-process actor library has been replaced
with [Akka](https://github.com/akkadotnet/akka.net).

## Use Cases
1. Deposit
2. Debit
3. Registering a transfer recipient internal to the bank
4. Transferring money to a registered recipient debits the sender and credits the receiver
5. Recurring maintenance fee for a period unless a qualified deposit found or a daily account balance threshold met.  *Typically 30 days but I've set it to 2 minutes for a faster feedback loop.  For a faster feedback loop adjust the [config](https://github.com/danne931/functional-programming-in-csharp-banking-sample/blob/main/CSharpWithLanguageExt/Config.cs) to 40 seconds or so*
6. Daily debit limit set by the customer
7. Lock/unlock debit card

## UI
I created a [simple](https://github.com/danne931/functional-programming-in-csharp-banking-sample/blob/main/FSharpWithAkka/wwwroot/js/account.js)
web page to test the use cases against an account.

[SignalR](https://dotnet.microsoft.com/en-us/apps/aspnet/signalr) is used to provide real-time feedback from actors to the UI:
- Overall account state
- History of transactions on the account
- Toggling between a transfer sender & receiver account demonstrates debits in one account and credits in the other

## Demonstration
![fp-dotnet-demonstration](https://github.com/danne931/functional-programming-in-csharp-banking-sample/assets/4181901/5a5fc264-e93b-4e42-9c13-dcfd3e19b059)

## Running the app
1. docker pull eventstore/eventstore
2. docker run --name esdb-node -it -p 2113:2113 -p 1113:1113 eventstore/eventstore:latest --insecure --run-projections=All --mem-db
3. run eventstoredb container
4. enable eventstoredb projections: make an empty JSON POST request to http://127.0.0.1:2113/projection/$by_event_type/command/enable
5. navigate to the C# or F# directories in this repo
6. dotnet run
7. make POST requests to http://localhost:PORT/accounts to create accounts ex:
    ```
    {
	      "FirstName": "Small",
	      "LastName": "Fish",
	      "EntityId": "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a1",
	      "Balance": 931,
	      "Currency": "USD"
    }
    ```
8. navigate to localhost in browser
9. restart eventstoredb container if restarting the server or switching between running C# and F# apps

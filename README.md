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
where you'll find a rewrite of all use cases in F#.  [F#'s type inference](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/type-inference) and [computation expressions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions) makes writing programs with functional programming quite a bit more comfortable than with C#.
If you've found your way around the C# directory then the F# directory structure should feel quite
similar.  Once there you will see we no longer need **Paul Louth**'s language-ext and his echo-process actor library has been replaced
with [Akka](https://github.com/akkadotnet/akka.net).

## Use Cases
1. Deposit
2. Debit
3. Registering a transfer recipient internal to the bank
4. Registering a transfer recipient in a 3rd party bank for domestic transfers
5. Transferring money to a registered recipient (internal to the bank) debits the sender and credits the receiver
6. Transferring money to an account in a mock 3rd party bank demonstrates resilience in face of intermittent network issues.  Integration with Akka circuit breaker allows our pending transfers to be reprocessed once the 3rd party bank is in a healthy state.
7. Recurring maintenance fee for a period unless a qualified deposit found or a daily account balance threshold met.  *Typically 30 days but I've set it to 2 minutes for a faster feedback loop.  For a faster feedback loop adjust the [config](https://github.com/danne931/functional-programming-in-csharp-banking-sample/blob/488cb3498b9255ef31145e94060049dac9eac3b1/CSharpWithLanguageExt/Account/AccountActor.cs#L45) to 40 seconds or so*
8. Daily debit limit set by the customer
9. Lock/unlock debit card

## UI
I created a [simple](https://github.com/danne931/functional-programming-in-csharp-banking-sample/blob/main/FSharpWithAkka/wwwroot/js/account.js)
web page to test the use cases against an account.

[SignalR](https://dotnet.microsoft.com/en-us/apps/aspnet/signalr) is used to provide real-time feedback from actors to the UI:
- Overall account state
- History of transactions on the account
- Toggling between a transfer sender & receiver account (internal to the bank) demonstrates debits in one account and credits in the other
- A system operations navbar displays circuit breaker open/closed status for domestic transfers to the 3rd party bank mock server
- When the circuit breaker closes, pending domestic transfers are reprocessed & corresponding Approved/Rejected events are interpolated into the table

## Demonstration
### Domestic transfers to a mock 3rd party bank server with circuit breaker integration:
![bank-domestic-transfer](https://github.com/danne931/functional-programming-in-csharp-banking-sample/assets/4181901/0c504ddd-8b56-4bcb-9001-107f4833e3d1)
### Transfers to accounts internal to the bank:
![bank-internal-transfer](https://github.com/danne931/functional-programming-in-csharp-banking-sample/assets/4181901/fd71e49f-f08b-4af1-9a64-3bac96490d98)

## Running the app
1. docker pull eventstore/eventstore
2. docker run --name esdb-node -it -p 2113:2113 -p 1113:1113 eventstore/eventstore:latest --insecure --run-projections=All --mem-db
3. run eventstoredb container
4. enable eventstoredb projections: make an empty JSON POST request to http://127.0.0.1:2113/projection/$by_event_type/command/enable
5. navigate to the C# or FSharpWithAkka directories in this repo & dotnet run
6. navigate to MockThirdPartyBankTransferReceiver and dotnet run
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

# Business Banking with Akka.NET

## Intro
This project utilizes the actor model and event sourcing via Akka.NET to build business banking functionality.  Event sourcing is implemented with [Akka.Persistence](https://getakka.net/articles/persistence/architecture.html) via PostgreSQL.  [Akka.Cluster.Sharding](https://getakka.net/articles/clustering/cluster-sharding.html) is used for account and employee actors.  Future/recurring actor message scheduling with PostgreSQL persistence is set up with [Quartz.NET](https://www.quartz-scheduler.net/).

## Use Cases
1. Automatic Balance Management
   - Maintain a zero balance after each transaction
   - Maintain a daily target balance by allowing a partner account to absorb excess cash or replenish cash to the target account
   - Distribute balance among multiple accounts within an org on a per-transaction, daily, or twice-monthly schedule
2. Employee Purchases
3. Move money between accounts within your organization
4. Transfer money immediately or on a future date to other organizations on the platform
5. Payment requests between organizations on the platform
6. Domestic transfers are sent to a Mock Domestic Transfer Processor service to mock the interaction of sending money to accounts outside the platform, such as an ACH transfer via Plaid.  Integration with Akka circuit breaker allows pending transfers to be reprocessed if they previously failed due to intermittent network issues.
7. Recurring maintenance fee for each billing cycle unless a qualified deposit found or a daily account balance threshold met.
8. Manage daily/monthly employee purchase limits per employee card
9. Monitor employee purchases and other activities on the platform
10. Billing statements issued for each billing cycle
11. Emails sent for account open/close, employee invites, billing statement, purchase declined, transfer deposited, etc.

![8-29-business-banking](https://github.com/user-attachments/assets/beb5de07-66e9-49be-abe2-5ea30ba06c7a)

## UI
The UI is built with React libraries for the F# landscape.  See [Feliz](https://zaid-ajaj.github.io/Feliz/#/Hooks/UseElmish).

[SignalR](https://dotnet.microsoft.com/en-us/apps/aspnet/signalr) is used to provide feedback from actors to the UI:
- Overall account state
- History of transactions on the account
- Toggling between a transfer sender & receiver account (internal to the bank) demonstrates debits in one account and credits in the other
- A system operations navbar displays circuit breaker open/closed status for domestic transfers to the 3rd party bank mock server
- When the circuit breaker closes, pending domestic transfers are reprocessed & corresponding Approved/Rejected events are interpolated into the table

## Demonstration
### Automatic Balance Management
![auto-balance-management](https://github.com/user-attachments/assets/f6be8238-b637-496e-b617-00a4d1b42ff2)
### Analytics
![analytics-timeseries](https://github.com/user-attachments/assets/ad4566f6-c2af-42cd-98bf-e06367437a84)
![analytics-top-n](https://github.com/user-attachments/assets/9359f424-9f1c-4e0e-90a8-bff3476683da)
### Transactions
![transactions-date](https://github.com/user-attachments/assets/f5f8d6da-93fe-451c-a739-775d6108c02f)
### More Demonstration GIFs included at the bottom of the README.

## Running with Kubernetes via minikube
1. Dependencies: [.NET 8](https://dotnet.microsoft.com/en-us/download), [minikube](https://minikube.sigs.k8s.io/docs/start/), [pulumi](https://www.pulumi.com/docs/install/), [Node.js 18](https://docs.npmjs.com/downloading-and-installing-node-js-and-npm)
2. `sh build.sh -t RunK8sApp`
3. Browser opens automatically after all K8s resources start up
4. Enable postgres port forwarding if you want to inspect postgres in a local client: `sh postgres-port-forward-k8s.sh` (Server=postgres;Database=akkabank;Uid=testuser;Pwd=testpass)
5. View Akka cluster and actor info via [Petabridge.Cmd](https://cmd.petabridge.com/articles/commands/cluster-commands.html):
   ```
   > minikube kubectl -- exec --stdin --tty account-cluster-0 -- /bin/bash
   > dotnet tool run pbm
   > cluster show
   > actor hierarchy
   ```

## Running with Docker
1. Dependencies: [.NET 8](https://dotnet.microsoft.com/en-us/download), [Node.js 18](https://docs.npmjs.com/downloading-and-installing-node-js-and-npm)
2. `sh build.sh -t RunDockerApp`
3. Navigate to localhost:3000
4. If you want to inspect postgres in a dashboard you can visit localhost:5008 (Server=postgres;Database=akkabank;Uid=postgres;Pwd=password)

## Running without Docker or K8s
1. Dependencies: [.NET 8](https://dotnet.microsoft.com/en-us/download), [Node.js 18](https://docs.npmjs.com/downloading-and-installing-node-js-and-npm), [PostgreSQL & psql](https://www.postgresql.org/download/)
2. Create a database (Server=localhost;Database=akkabank;Uid=postgres;Pwd=password)
3. Seed the database: `psql postgres < Infrastructure/Migrations/*.sql`
4. `cd` into ./Web, ./Account.Service, ./Scheduler.Service, & ./MockThirdPartyBankTransferReceiver & `dotnet run` in each
5. `cd` into ./UI and `npm run build` or `npm start`

## Deploying to Azure AKS
1. Dependencies: [.NET 8](https://dotnet.microsoft.com/en-us/download), [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli), [Pulumi](https://www.pulumi.com/docs/install/), [Node.js 18](https://docs.npmjs.com/downloading-and-installing-node-js-and-npm)
2. `sh build.sh -t DeployAllStaging` (You will be prompted to sign in to Pulumi & Azure CLI)
3. One pulumi stack of Azure AKS resources and another Pulumi stack for K8s resources will be created.  A [Pulumi ESC](https://www.pulumi.com/product/esc/) staging environment will be created and app environment configuration will be set.  Your local kubeconfig file will be modified to include details needed to connect to the AKS cluster.  See `kubectl get all --namespace akkabank` & `kubectl get all --namespace app-routing-system`.  App images are currently being pulled from my public docker hub repos.  Ingress is partially configured - An IP will be exported to the console when Pulumi finishes creating resources.

## Running tests
1. `sh build.sh -t Test`

## Archive
Inspiration for this project stemmed from reading [Functional Programming in C#](https://www.manning.com/books/functional-programming-in-c-sharp-second-edition)
by **Enrico Buonanno**.  

The first iteration of this project [(see CSharpWithLanguageExt directory)](https://github.com/danne931/akka-dotnet-bank/tree/main/Archive/CSharpWithLanguageExt) expands on **Enrico Buonanno**'s banking account example [actor](https://github.com/la-yumba/functional-csharp-code-2/blob/master/Examples/Chapter19/Boc/AccountProcess.cs)
and [domain logic](https://github.com/la-yumba/functional-csharp-code-2/blob/master/Examples/Chapter13/Domain/Account.cs) to include
additional business use cases as well as integration with more tech such as [EventStoreDB](https://www.eventstore.com/eventstoredb) and the
de facto library for functional programming in C#, [language-ext](https://github.com/louthy/language-ext).

The second iteration [(see FSharpWithAkka directory)](https://github.com/danne931/akka-dotnet-bank/tree/main/Archive/FSharpWithAkka) is close to a one-to-one representation of the CSharpWithLanguageExt directory, with all use cases rewritten in F# and **Paul Louth**'s echo-process actor library replaced with Akka.  I saw that [F#'s type inference](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/type-inference), [computation expressions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions) and immutable data structures made writing programs with typed functional programming more second nature than with C# so I decided to continue with it for the final iteration.

## Additional Demonstration
### Employee Card Management & Employee History
![card-purchase-limits-and-nickname](https://github.com/user-attachments/assets/821518a2-6c29-4152-9ba9-1f1dd3aef969)
![card-lock-employee-history](https://github.com/user-attachments/assets/303676ba-3534-487b-bc7f-3eb99ea87aab)
![employee-history](https://github.com/user-attachments/assets/30c5f2af-d717-405d-b080-cff13bb2af84)
![employee-management](https://github.com/user-attachments/assets/e9486950-2553-4447-b101-22d53a90ad9b)

### Transfers within an org & between orgs on the platform
![transfer-internal](https://github.com/user-attachments/assets/06708061-822e-417b-94bf-9406f324443d)

### Transactions
![transactions-money-flow](https://github.com/user-attachments/assets/aee4c129-892a-4d40-b676-4c2edaff729c)
![transactions-transfers](https://github.com/user-attachments/assets/7fd0676f-d2c1-44bf-9cae-3d6bc58d48d0)
![transactions-initiated-by](https://github.com/user-attachments/assets/a07cf94f-d915-4839-adb9-58f900b5aacf)
![transactions-employee-card](https://github.com/user-attachments/assets/acd51183-8cc3-49d5-abe0-0d8d7b7613de)
![transactions-category](https://github.com/user-attachments/assets/772f5fd1-fba6-48e9-84ad-710dda18e8d6)
![transaction-back-button](https://github.com/user-attachments/assets/99437c22-e484-4d57-957e-3bb5253784e1)

### Domestic transfers to a mock 3rd party bank server with circuit breaker integration:
![bank-domestic-transfer-1-30](https://github.com/danne931/akka-dotnet-bank/assets/4181901/8d200b11-99d8-4e8f-98d4-0ab5941f1447)

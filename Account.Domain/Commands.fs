namespace Bank.Account.Domain

open Validus

open Lib.SharedTypes
open Lib.Validators

type CreateAccountInput = {
   Name: string
   //Depository: AccountDepository
   Currency: Currency
   AccountId: AccountId
   OrgId: OrgId
   InitiatedBy: InitiatedById
}

type CreateAccountCommand = Command<CreateAccountInput>

module CreateAccountCommand =
   let create (data: CreateAccountInput) =
      Command.create
         (AccountId.toEntityId data.AccountId)
         data.OrgId
         (CorrelationId.create ())
         data.InitiatedBy
         data

   let toEvent
      (cmd: CreateAccountCommand)
      : ValidationResult<BankEvent<CreatedAccount>>
      =
      validate {
         let input = cmd.Data
         let routingNumberInput = "123456789"
         // TODO: Provide a workflow to check account numbers against DB
         let accountNumberInput =
            let random = System.Random()

            List.init 15 (fun _ -> random.Next(1, 9) |> string)
            |> String.concat ""

         let! accountName = accountNameValidator input.Name

         and! accountNumber =
            accountNumberValidator "Account Number" accountNumberInput

         and! routingNumber =
            routingNumberValidator "Routing Number" routingNumberInput

         return
            BankEvent.create2<CreateAccountInput, CreatedAccount> cmd {
               Name = accountName
               //Depository = input.Depository
               Balance = 0m
               Currency = input.Currency
               RoutingNumber = routingNumber
               AccountNumber = accountNumber
            }
      }

type DepositCashInput = {
   Amount: decimal
   Origin: string option
}

type DepositCashCommand = Command<DepositCashInput>

module DepositCashCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: DepositCashInput)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent
      (cmd: DepositCashCommand)
      : ValidationResult<BankEvent<DepositedCash>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Deposit amount" input.Amount

         return
            BankEvent.create2<DepositCashInput, DepositedCash> cmd {
               Amount = input.Amount
               Origin = input.Origin |> Option.defaultValue "ATM"
            }
      }

type DebitCommand = Command<DebitedAccount>

module DebitCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (correlationId: CorrelationId)
      (initiatedBy: InitiatedById)
      (data: DebitedAccount)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         correlationId
         initiatedBy
         data

   let toEvent
      (cmd: DebitCommand)
      : ValidationResult<BankEvent<DebitedAccount>>
      =
      validate {
         let input = cmd.Data
         let! _ = amountValidator "Debit amount" input.Amount
         let! _ = dateNotDefaultValidator "Date" input.Date
         let! _ = originValidator input.Origin

         return BankEvent.create<DebitedAccount> cmd
      }

type MaintenanceFeeCommand = Command<MaintenanceFeeDebited>

module MaintenanceFeeCommand =
   let create (accountId: AccountId, orgId: OrgId) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         (System.Guid.Empty |> EmployeeId |> InitiatedById)
         {
            Amount = MaintenanceFee.RecurringDebitAmount
         }

   let toEvent
      (cmd: MaintenanceFeeCommand)
      : ValidationResult<BankEvent<MaintenanceFeeDebited>>
      =
      Ok <| BankEvent.create<MaintenanceFeeDebited> cmd


type SkipMaintenanceFeeCommand = Command<MaintenanceFeeSkipped>

module SkipMaintenanceFeeCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (data: MaintenanceFeeSkipped)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         (System.Guid.Empty |> EmployeeId |> InitiatedById)
         data

   let toEvent
      (cmd: SkipMaintenanceFeeCommand)
      : ValidationResult<BankEvent<MaintenanceFeeSkipped>>
      =
      Ok <| BankEvent.create<MaintenanceFeeSkipped> cmd

type CloseAccountCommand = Command<AccountClosed>

module CloseAccountCommand =
   let create
      (accountId: AccountId, orgId: OrgId)
      (initiatedBy: InitiatedById)
      (data: AccountClosed)
      =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         initiatedBy
         data

   let toEvent
      (cmd: CloseAccountCommand)
      : ValidationResult<BankEvent<AccountClosed>>
      =
      Ok <| BankEvent.create<AccountClosed> cmd

type StartBillingCycleCommand = Command<BillingCycleStarted>

module StartBillingCycleCommand =
   let create (accountId: AccountId, orgId: OrgId) (data: BillingCycleStarted) =
      Command.create
         (AccountId.toEntityId accountId)
         orgId
         (CorrelationId.create ())
         (System.Guid.Empty |> EmployeeId |> InitiatedById)
         data

   let toEvent
      (cmd: StartBillingCycleCommand)
      : ValidationResult<BankEvent<BillingCycleStarted>>
      =
      Ok <| BankEvent.create<BillingCycleStarted> cmd

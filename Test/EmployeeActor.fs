module EmployeeActorTests

open System
open Expecto
open Akkling
open Akkling.Cluster.Sharding
open Akka.Actor
open Akka.Persistence.Extras

open Util
open ActorUtil
open Bank.Account.Domain
open Bank.Employee.Domain

module Stub = EmployeeStub

// NOTE: Change default snapshot store from local file system
//       to in memory.
let config =
   Configuration.parse
      """
      akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
      """

// Mock PersistenceSupervisor message wrapping for command
// intended to be persisted
let envelope msg =
   ConfirmableMessageEnvelope(Int64.MinValue, "", msg)

let initMockAccountActor (tck: TestKit.Tck) =
   let handler (ctx: Actor<_>) (msg: obj) =
      match msg with
      | :? ShardEnvelope as envelope ->
         match envelope.Message with
         | :? AccountMessage as msg ->
            match msg with
            | AccountMessage.StateChange(AccountCommand.Debit cmd) ->
               cmd
               |> AccountCommand.Debit
               |> AccountMessage.StateChange
               |> tck.TestActor.Tell

               ignored ()
            | msg -> unhandled msg
         | msg -> unhandled msg
      | msg -> unhandled msg

   spawn tck "account-mock" <| props (actorOf2 handler)

// Mock PersistenceSupervisor forwards messages to EmployeeActor
// & wraps StateChange messages intended to be persisted.
let mockPersistenceSupervisorProps
   (spawnChild: Actor<EmployeeMessage> -> IActorRef<obj>)
   =
   let init (ctx: Actor<EmployeeMessage>) =
      let child = spawnChild ctx

      actor {
         let! msg = ctx.Receive()

         if EmployeeActor.isPersistableMessage msg then
            child <<! envelope msg
         else
            child <<! msg

         return ignored ()
      }

   props init

let init (tck: TestKit.Tck) =
   let emailProbe = tck.CreateTestProbe()

   let getAccountRef = getAccountEntityRef (initMockAccountActor tck)

   let getEmailActor (_: ActorSystem) =
      (typed emailProbe :> IActorRef<EmailActor.EmailMessage>)

   let prop =
      mockPersistenceSupervisorProps (fun ctx ->
         spawn ctx ActorMetadata.employee.Name
         <| EmployeeActor.actorProps getAccountRef getEmailActor)

   let employeeActor = spawn tck ActorMetadata.employee.Name prop

   {|
      employeeActor = employeeActor
      emailProbe = emailProbe
   |}

[<Tests>]
let tests =
   testList "Employee Actor" [
      akkaTest "Create Account Owner should send an employee invite email"
      <| Some config
      <| fun tck ->
         let o = init tck

         let cmd =
            EmployeeCommand.CreateAccountOwner Stub.command.createAccountOwner

         o.employeeActor <! EmployeeMessage.StateChange cmd
         o.employeeActor <! EmployeeMessage.GetEmployee

         let state = tck.ExpectMsg<Option<Employee>>()

         let msg = o.emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.EmployeeInvite invite ->
            Expect.equal
               (state |> Option.map _.Email)
               (Some invite.Email)
               "EmailActor should receive EmployeeInvite message"
         | msg ->
            Expect.isTrue
               false
               $"Expected EmployeeInvite EmailMessage. Received {msg}"

      akkaTest "Create employee should send an employee invite email"
      <| Some config
      <| fun tck ->
         let o = init tck
         let cmd = EmployeeCommand.CreateEmployee Stub.command.createEmployee
         o.employeeActor <! EmployeeMessage.StateChange cmd
         o.employeeActor <! EmployeeMessage.GetEmployee

         let state = tck.ExpectMsg<Option<Employee>>()
         let employee = Expect.wantSome state ""

         let msg = o.emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.EmployeeInvite invite ->
            Expect.equal
               invite.Email
               employee.Email
               "EmailActor should receive EmployeeInvite message"
         | msg ->
            Expect.isTrue
               false
               $"Expected EmployeeInvite EmailMessage. Received {msg}"


      akkaTest
         "Creating an employee with card details should create a card
          once the employee confirms their invite email."
      <| Some config
      <| fun tck ->
         let o = init tck

         let employeeInviteSupplementaryCardInfo = {
            DailyPurchaseLimit = 3000m
            MonthlyPurchaseLimit = 15_000m
            LinkedAccountId = Stub.accountId
         }

         let cmd = {
            Stub.command.createEmployee with
               Data.CardInfo = Some employeeInviteSupplementaryCardInfo
         }

         let cmd = EmployeeCommand.CreateEmployee cmd
         o.employeeActor <! EmployeeMessage.StateChange cmd
         o.employeeActor <! EmployeeMessage.GetEmployee

         let state = tck.ExpectMsg<Option<Employee>>()
         let employee = Expect.wantSome state ""

         Expect.contains
            employee.OnboardingTasks
            (EmployeeOnboardingTask.CreateCard
               employeeInviteSupplementaryCardInfo)
            "CreateCard should show up as an employee onboarding task"

         let confirmInvite =
            EmployeeCommand.ConfirmInvitation Stub.command.confirmInvite

         o.employeeActor <! EmployeeMessage.StateChange confirmInvite

         TestKit.within tck (TimeSpan.FromSeconds 5) (fun () ->
            o.employeeActor <! EmployeeMessage.GetEmployee

            let state = tck.ExpectMsg<Option<Employee>>()
            let employee = Expect.wantSome state ""

            Expect.hasLength employee.OnboardingTasks 0 "Onboarding tasks = 0"

            let card = employee.Cards.Head().Value

            Expect.equal
               card.DailyPurchaseLimit
               employeeInviteSupplementaryCardInfo.DailyPurchaseLimit
               "Daily purchase limit = amount set during employee creation"

            Expect.equal
               card.MonthlyPurchaseLimit
               employeeInviteSupplementaryCardInfo.MonthlyPurchaseLimit
               "Monthly purchase limit = amount set during employee creation"

            Expect.equal
               card.AccountId
               employeeInviteSupplementaryCardInfo.LinkedAccountId
               "Card linked to selected account associated with the org"

            Expect.equal card.Status CardStatus.Active "Card is active")

      akkaTest
         "Updating an employee role, & including card details should 
          create a card."
      <| Some config
      <| fun tck ->
         let o = init tck

         for cmd in Stub.initCommands do
            o.employeeActor <! EmployeeMessage.StateChange cmd

         o.employeeActor <! EmployeeMessage.GetEmployee
         let state = tck.ExpectMsg<Option<Employee>>()
         let employee = Expect.wantSome state ""
         Expect.hasLength employee.Cards 1 "Init employee with 1 card"

         let updateRoleMsg =
            Stub.command.updateRoleWithSupplementaryCardInfo
            |> EmployeeCommand.UpdateRole
            |> EmployeeMessage.StateChange

         o.employeeActor <! updateRoleMsg

         TestKit.within tck (TimeSpan.FromSeconds 5) (fun () ->
            o.employeeActor <! EmployeeMessage.GetEmployee

            let state = tck.ExpectMsg<Option<Employee>>()
            let employee = Expect.wantSome state ""

            Expect.hasLength employee.Cards 2 "Card count increased to 2")

      akkaTest
         "Refreshing an expired employee invite token should send an employee invite email"
      <| Some config
      <| fun tck ->
         let o = init tck
         let cmd = EmployeeCommand.CreateEmployee Stub.command.createEmployee
         o.employeeActor <! EmployeeMessage.StateChange cmd
         o.employeeActor <! EmployeeMessage.GetEmployee

         let state = tck.ExpectMsg<Option<Employee>>()
         o.emailProbe.ExpectMsg<EmailActor.EmailMessage>() |> ignore

         let cmd =
            EmployeeCommand.RefreshInvitationToken
               Stub.command.refreshInviteToken

         o.employeeActor <! EmployeeMessage.StateChange cmd

         let employee = Expect.wantSome state ""

         let msg = o.emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.EmployeeInvite invite ->
            Expect.equal
               invite.Email
               employee.Email
               "EmailActor should receive EmployeeInvite message"
         | msg ->
            Expect.isTrue
               false
               $"Expected EmployeeInvite EmailMessage. Received {msg}"

      akkaTest "A valid debit request should be promoted to the AccountActor"
      <| Some config
      <| fun tck ->
         let o = init tck

         for cmd in Stub.initCommands do
            o.employeeActor <! EmployeeMessage.StateChange cmd

         let amount = 150m

         let debitCmd = Stub.command.debit amount
         let debitMsg = EmployeeCommand.DebitRequest debitCmd

         o.employeeActor <! EmployeeMessage.StateChange debitMsg

         let msg = tck.ExpectMsg<AccountMessage>()

         match msg with
         | AccountMessage.StateChange(AccountCommand.Debit cmd) ->
            Expect.equal
               cmd.Data.Amount
               amount
               "Account debit amount = employee debit request amount"

            Expect.equal
               cmd.Data.EmployeePurchaseReference.CardId
               debitCmd.Data.CardId
               "Debit card reference = employee debit card"
         | _ ->
            Expect.isTrue false "Expected Debit command sent to account actor"

      akkaTest
         "A failed debit due to exceeding daily debit allowance should notify the EmailActor"
      <| Some config
      <| fun tck ->
         let o = init tck

         for cmd in Stub.initCommands do
            o.employeeActor <! EmployeeMessage.StateChange cmd

         let cmd =
            EmployeeCommand.LimitDailyDebits
            <| Stub.command.limitDailyDebits 100m

         o.employeeActor <! EmployeeMessage.StateChange cmd
         o.employeeActor <! EmployeeMessage.GetEmployee

         let state = tck.ExpectMsg<Option<Employee>>()
         let employee = Expect.wantSome state ""

         Expect.equal
            employee.Cards[Stub.cardId].DailyPurchaseLimit
            100m
            "Account state should be configured with the daily debit limit"

         let expectedAccrued = 98m

         let debitCmd = Stub.command.debit expectedAccrued
         let debitMsg = EmployeeCommand.DebitRequest debitCmd

         // Mock the approval sent from the account actor
         let approveDebitCmd = Stub.command.approveDebit

         let approveDebitCmd = {
            approveDebitCmd with
               Data.Info.Amount = debitCmd.Data.Amount
         }

         let approveDebitMsg = EmployeeCommand.ApproveDebit approveDebitCmd

         let debitToBeRejectedMsg =
            EmployeeCommand.DebitRequest <| Stub.command.debit 33m

         o.employeeActor <! EmployeeMessage.StateChange debitMsg
         o.employeeActor <! EmployeeMessage.StateChange approveDebitMsg

         o.employeeActor <! EmployeeMessage.StateChange debitToBeRejectedMsg

         o.emailProbe.ExpectMsg<EmailActor.EmailMessage>() |> ignore
         let msg = o.emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.DebitDeclinedExceededDailyDebit(_, accrued, _) ->
            Expect.equal
               accrued
               expectedAccrued
               "Should reject purchases over the daily limit"
         | msg ->
            Expect.isTrue
               false
               $"Expected DebitDeclinedExceedingDailyDebit EmailMessage. Received {msg}"
   ]

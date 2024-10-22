module AutomaticBalanceManagementTests

open System
open Expecto

open Bank.Transfer.Domain
open AutomaticTransfer
open Lib.SharedTypes

module Stub =
   let orgId = Guid.NewGuid() |> OrgId

   let accountA = Guid.NewGuid() |> AccountId
   let accountB = Guid.NewGuid() |> AccountId
   let accountC = Guid.NewGuid() |> AccountId
   let accountD = Guid.NewGuid() |> AccountId
   let accountE = Guid.NewGuid() |> AccountId

   let sender: InternalTransferSender = {
      Name = "Operations"
      AccountId = accountA
      OrgId = orgId
   }

   let recipient: InternalTransferRecipient = {
      Name = "Savings"
      AccountId = accountB
      OrgId = orgId
   }

   let percentDistributionDestinationAccount
      : UnvalidatedDistributionDestinationAccount = {
      Recipient = recipient
      ProposedPercentAllocated = 100m
   }

   let targetBalanceRule = {
      TargetAccount = {
         Name = "Operations"
         AccountId = accountA
         OrgId = orgId
      }
      ManagingPartnerAccount = {
         Name = "Savings"
         AccountId = accountB
         OrgId = orgId
      }
      TargetAccountBalance = (PositiveAmount.create 113_000m).Value
      TargetBalanceRange = None
   }

   let createZeroBalanceConfig accountIdA accountIdB =
      let rule: ZeroBalanceRule = {
         Sender = { sender with AccountId = accountIdA }
         Recipient = {
            recipient with
               AccountId = accountIdB
         }
      }

      {
         Id = Guid.NewGuid()
         Info = AutomaticTransferRule.ZeroBalance rule
      }

   let createTargetBalanceConfig accountIdA accountIdB =
      let rule = {
         TargetAccount = {
            Name = "Operations"
            AccountId = accountIdA
            OrgId = orgId
         }
         ManagingPartnerAccount = {
            Name = "Savings"
            AccountId = accountIdB
            OrgId = orgId
         }
         TargetAccountBalance = (PositiveAmount.create 113_000m).Value
         TargetBalanceRange = None
      }

      {
         Id = Guid.NewGuid()
         Info = AutomaticTransferRule.TargetBalance rule
      }

   let createPercentDistributionConfig a b c =
      let sender = { sender with AccountId = a }

      let rule =
         PercentDistributionRule.create Frequency.PerTransaction sender [
            {
               Recipient = { recipient with AccountId = b }
               ProposedPercentAllocated = 50m
            }
            {
               Recipient = { recipient with AccountId = c }
               ProposedPercentAllocated = 50m
            }
         ]

      let rule = (Result.toOption rule).Value

      {
         Id = Guid.NewGuid()
         Info = AutomaticTransferRule.PercentDistribution rule
      }

type PercentErr = PercentDistributionRule.ValidationError

[<Tests>]
let tests =
   testList "Automatic transfer domain" [
      test "Percent distribution should have at least 1 destination" {
         let res =
            PercentDistributionRule.create Frequency.PerTransaction Stub.sender []

         Expect.equal
            res
            (Error PercentErr.LessThan1Destination)
            "needs >= 1 destination"

         let res =
            PercentDistributionRule.create Frequency.PerTransaction Stub.sender [
               Stub.percentDistributionDestinationAccount
            ]

         Expect.isOk res "accept with at least 1 valid destination"
      }

      test "Percent distribution sender should be includes in destinations" {
         let res =
            PercentDistributionRule.create Frequency.PerTransaction Stub.sender [
               {
                  Recipient = {
                     Stub.recipient with
                        AccountId = Stub.sender.AccountId
                  }
                  ProposedPercentAllocated = 50m
               }
               {
                  Recipient = Stub.recipient
                  ProposedPercentAllocated = 50m
               }
            ]

         Expect.equal
            res
            (Error PercentErr.SenderIsDestination)
            "sender can not be recipient"
      }

      test "Percent distribution allocation invalid when less than 100" {
         let res =
            PercentDistributionRule.create Frequency.PerTransaction Stub.sender [
               {
                  Recipient = {
                     Stub.recipient with
                        AccountId = Guid.NewGuid() |> AccountId
                  }
                  ProposedPercentAllocated = 49m
               }
               {
                  Recipient = Stub.recipient
                  ProposedPercentAllocated = 50m
               }
            ]

         Expect.equal
            res
            (Error(PercentErr.TotalPercentAllocatedNot100 99m))
            "percent allocated expected to be 100"
      }

      test "Percent distribution allocation invalid when greater than 100" {
         let res =
            PercentDistributionRule.create Frequency.PerTransaction Stub.sender [
               {
                  Recipient = {
                     Stub.recipient with
                        AccountId = Guid.NewGuid() |> AccountId
                  }
                  ProposedPercentAllocated = 51m
               }
               {
                  Recipient = Stub.recipient
                  ProposedPercentAllocated = 50m
               }
            ]

         Expect.equal
            res
            (Error(PercentErr.TotalPercentAllocatedNot100 101m))
            "percent allocated expected to be 100"
      }

      test "Percent distribution allocation should equal 100" {
         let res =
            PercentDistributionRule.create Frequency.PerTransaction Stub.sender [
               {
                  Recipient = {
                     Stub.recipient with
                        AccountId = Guid.NewGuid() |> AccountId
                  }
                  ProposedPercentAllocated = 50m
               }
               {
                  Recipient = Stub.recipient
                  ProposedPercentAllocated = 50m
               }
            ]

         Expect.isOk res "percent allocated expected to be 100"
      }

      test "Percent distribution may not include a negative allocation" {
         let res =
            PercentDistributionRule.create Frequency.PerTransaction Stub.sender [
               {
                  Recipient = {
                     Stub.recipient with
                        AccountId = Guid.NewGuid() |> AccountId
                  }
                  ProposedPercentAllocated = 150m
               }
               {
                  Recipient = Stub.recipient
                  ProposedPercentAllocated = -50m
               }
            ]

         Expect.equal
            res
            (Error PercentErr.ContainsNegativePercentAllocation)
            "percent allocated may not include a negative %"
      }

      test "Percent distribution rule computeTransfer" {
         let recipient1 = {
            Stub.recipient with
               AccountId = Guid.NewGuid() |> AccountId
         }

         let recipient2 = Stub.recipient

         let res =
            PercentDistributionRule.create Frequency.PerTransaction Stub.sender [
               {
                  Recipient = recipient1
                  ProposedPercentAllocated = 75m
               }
               {
                  Recipient = recipient2
                  ProposedPercentAllocated = 25m
               }
            ]

         let rule = Expect.wantOk res ""

         let balance = (PositiveAmount.create 1_000m).Value
         let computed = PercentDistributionRule.computeTransfer rule balance

         let findComputed (accountId: AccountId) =
            computed |> List.find (fun t -> t.Recipient.AccountId = accountId)

         let compute1 = findComputed recipient1.AccountId
         Expect.equal compute1.Amount (PositiveAmount.create 750m).Value ""

         let compute2 = findComputed recipient2.AccountId
         Expect.equal compute2.Amount (PositiveAmount.create 250m).Value ""
      }

      test "Zero balance rule computeTransfer" {
         let rule: ZeroBalanceRule = {
            Sender = Stub.sender
            Recipient = Stub.recipient
         }

         let balance = (PositiveAmount.create 1500m).Value
         let compute = ZeroBalanceRule.computeTransfer rule balance

         Expect.equal compute.Amount balance ""
      }

      test "Target balance rule no transfer if target balance met" {
         let rule = Stub.targetBalanceRule

         let balance = PositiveAmount.get rule.TargetAccountBalance
         let compute = TargetBalanceRule.computeTransfer rule balance
         Expect.isNone compute "no computed transfer if target balance"

         let compute = TargetBalanceRule.computeTransfer rule (balance - 1m)
         Expect.isSome compute "computed transfer if balance dips below target"

         let compute = TargetBalanceRule.computeTransfer rule (balance + 1m)

         Expect.isSome
            compute
            "computed transfer if balance raises above target"
      }

      test "Target balance rule no transfer if target balance range met" {
         let rule = Stub.targetBalanceRule
         let balance = PositiveAmount.get rule.TargetAccountBalance

         let rule = {
            rule with
               TargetBalanceRange =
                  Some {
                     LowerBound = (PositiveAmount.create (balance - 100m)).Value
                     UpperBound = (PositiveAmount.create (balance + 150m)).Value
                  }
         }

         let compute = TargetBalanceRule.computeTransfer rule balance
         Expect.isNone compute ""

         let compute = TargetBalanceRule.computeTransfer rule (balance - 50m)
         Expect.isNone compute ""

         let compute = TargetBalanceRule.computeTransfer rule (balance + 50m)
         Expect.isNone compute ""

         let compute = TargetBalanceRule.computeTransfer rule (balance - 101m)

         Expect.isSome
            compute
            "computed transfer if balance dips below lower range"

         let compute = TargetBalanceRule.computeTransfer rule (balance + 151m)

         Expect.isSome
            compute
            "computed transfer if balance raises above upper range"
      }

      test
         "Cycle detection prevents adding a rule where transfer
            direction is A->B if a rule where B->A exists already" {
         let existing = [
            Stub.createZeroBalanceConfig Stub.accountA Stub.accountB
         ]

         let configToAdd =
            Stub.createZeroBalanceConfig Stub.accountB Stub.accountA

         let hasCycle = CycleDetection.cycleDetected configToAdd existing
         Expect.isTrue hasCycle "A->B, B->A = cycle"

         let configToAdd =
            Stub.createZeroBalanceConfig Stub.accountC Stub.accountA

         let hasCycle = CycleDetection.cycleDetected configToAdd existing
         Expect.isFalse hasCycle "A->B, C->A = no cycle"

         let configToAdd =
            Stub.createPercentDistributionConfig
               Stub.accountC
               Stub.accountB
               Stub.accountA

         let hasCycle = CycleDetection.cycleDetected configToAdd existing
         Expect.isFalse hasCycle "A->B, C->B, C->A = no cycle"

         let configToAdd =
            Stub.createTargetBalanceConfig Stub.accountB Stub.accountC

         let hasCycle = CycleDetection.cycleDetected configToAdd existing
         Expect.isFalse hasCycle "A->B, B<->C = no cycle"

         let configToAdd =
            Stub.createTargetBalanceConfig Stub.accountB Stub.accountA

         let hasCycle = CycleDetection.cycleDetected configToAdd existing
         Expect.isTrue hasCycle "A->B, B<->A = cycle"
      }

      test "Cycle detection accommodates updating an existing rule" {
         let config = Stub.createZeroBalanceConfig Stub.accountA Stub.accountB
         let existing = [ config ]

         let configToAdd =
            Stub.createZeroBalanceConfig Stub.accountB Stub.accountA

         let hasCycle = CycleDetection.cycleDetected configToAdd existing
         Expect.isTrue hasCycle "A->B, B->A = cycle"

         // configToAdd has same Id as config, so consider it an update
         let configToAdd =
            Stub.createZeroBalanceConfig Stub.accountB Stub.accountA

         let configToAdd = { configToAdd with Id = config.Id }
         let hasCycle = CycleDetection.cycleDetected configToAdd existing
         Expect.isFalse hasCycle "A->B, B->A = no cycle update existing"
      }

      test
         "Cycle detection prevents adding a rule where transfer
            direction is A->B if a rule exists where the transfer path will
            eventually lead back to A, such as A->B, B->C, C->A" {
         // A->B
         let zero = Stub.createZeroBalanceConfig Stub.accountA Stub.accountB
         // B<->C
         let target = Stub.createTargetBalanceConfig Stub.accountB Stub.accountC
         // C->A, C->D
         let percentDist =
            Stub.createPercentDistributionConfig
               Stub.accountC
               Stub.accountA
               Stub.accountD

         let existing = [ zero; target ]
         let hasCycle = CycleDetection.cycleDetected percentDist existing
         // cycle = A->B->C->A
         Expect.isTrue hasCycle "A->B, B<->C, C->A, C->D = cycle"

         // B<->D
         let target = Stub.createTargetBalanceConfig Stub.accountB Stub.accountD
         let existing = [ zero; target ]
         let hasCycle = CycleDetection.cycleDetected percentDist existing
         Expect.isFalse hasCycle "A->B, B<->D, C->A, C->D = no cycle"

         // D->A
         let zero2 = Stub.createZeroBalanceConfig Stub.accountD Stub.accountA
         let existing = [ zero; target; percentDist ]
         let hasCycle = CycleDetection.cycleDetected zero2 existing
         // cycle = D->A->B->D
         Expect.isTrue hasCycle "A->B, B<->D, C->A, C->D, D->A = cycle"

         // D->E
         let zero2 = Stub.createZeroBalanceConfig Stub.accountD Stub.accountE
         let existing = [ zero; target; percentDist ]
         let hasCycle = CycleDetection.cycleDetected zero2 existing
         Expect.isFalse hasCycle "A->B, B<->D, C->A, C->D, D->E = no cycle"
      }
   ]

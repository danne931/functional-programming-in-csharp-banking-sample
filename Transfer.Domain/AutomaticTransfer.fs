module AutomaticTransfer

open Validus
open System

open Lib.SharedTypes
open Bank.Transfer.Domain

/// Automated internal transfers (i.e. system-produced transfers
/// based on per-account balance management configuration)
type AutoTransfer = {
   Sender: InternalTransferSender
   Recipient: InternalTransferRecipient
   Amount: PositiveAmount.T
}

/// Move 100% of account balance to an account within the org after each transaction.
type ZeroBalanceRule = {
   Sender: InternalTransferSender
   Recipient: InternalTransferRecipient
}

module ZeroBalanceRule =
   let computeTransfer
      (rule: ZeroBalanceRule)
      (fromBalance: PositiveAmount.T)
      : AutoTransfer
      =
      {
         Amount = fromBalance
         Sender = rule.Sender
         Recipient = rule.Recipient
      }

// NOTE:
// This will currently happen around the same time for every account.
// Perhaps consider providing a time zone for the organization
// and allowing the user to select a preferred time of day.
// Will have to store more jobs if this update is implemented.
[<RequireQualifiedAccess>]
type CronSchedule =
   /// Occurs at 8AM daily.
   | Daily
   /// Occurs at 8AM on the 1st and 15th of the month.
   | TwiceMonthly

[<RequireQualifiedAccess>]
type Frequency =
   | PerTransaction
   | Schedule of CronSchedule

type UnvalidatedDistributionDestinationAccount = {
   Recipient: InternalTransferRecipient
   ProposedPercentAllocated: decimal
}

type PercentDistributionDestinationAccount = {
   Recipient: InternalTransferRecipient
   PercentAllocated: PositiveAmount.T
}

/// Allocate 100% of account balance split amongst accounts within the org.
module PercentDistributionRule =
   type T =
      private | T of
         {|
            Sender: InternalTransferSender
            Frequency: Frequency
            DestinationAccounts: PercentDistributionDestinationAccount list
         |}

   let get (T value) = value

   let create
      (frequency: Frequency)
      (sender: InternalTransferSender)
      (unvalidatedDestination: UnvalidatedDistributionDestinationAccount list)
      : ValidationResult<T>
      =
      validate {
         let field = "destination"
         let! _ = Check.List.greaterThanLen 1 field unvalidatedDestination

         let percentSum =
            unvalidatedDestination |> List.sumBy _.ProposedPercentAllocated

         let! _ = Check.Decimal.equals 100m field percentSum

         let senderIsNotARecipient =
            unvalidatedDestination
            |> List.forall (fun o -> o.Recipient.AccountId <> sender.AccountId)

         let! destination =
            if senderIsNotARecipient then
               Ok unvalidatedDestination
            else
               Error
               <| ValidationErrors.create field [
                  "Sender should not be included as a destination."
               ]

         let destination =
            destination
            |> List.choose (fun o ->
               PositiveAmount.create o.ProposedPercentAllocated
               |> Option.map (fun allocated -> {
                  Recipient = o.Recipient
                  PercentAllocated = allocated
               }))

         let! destination =
            if destination.Length = unvalidatedDestination.Length then
               Ok destination
            else
               Error
               <| ValidationErrors.create field [
                  "Percent allocated must be a positive amount for each
                  destination account."
               ]

         return
            T {|
               Sender = sender
               Frequency = frequency
               DestinationAccounts = destination
            |}
      }

   let computeTransfer
      (rule: T)
      (fromBalance: PositiveAmount.T)
      : AutoTransfer list
      =
      let rule = get rule

      rule.DestinationAccounts
      |> List.map (fun o -> {
         Sender = rule.Sender
         Recipient = o.Recipient
         Amount =
            PositiveAmount.map2
               (fun (fromBalance, percentAllocated) ->
                  fromBalance * percentAllocated / 100m)
               fromBalance
               o.PercentAllocated
      })

type TargetBalanceRange = {
   LowerBound: PositiveAmount.T
   UpperBound: PositiveAmount.T
}

type BiDirectionalTransferContact = {
   Name: string
   AccountId: AccountId
   OrgId: OrgId
}

type TargetBalanceRule = {
   /// This account acts as a partner in maintaining the balance
   /// for the target account.  It will absorb excess cash from
   /// the account or replenish cash to the account.
   ManagingPartnerAccount: BiDirectionalTransferContact
   TargetAccount: BiDirectionalTransferContact
   /// If the balance drops below this target, we will restore it by
   /// pulling funds from ManagingPartnerAccount.
   /// If the balance rises above this target, we will restore it by
   /// transferring funds to ManagingPartnerAccount.
   TargetAccountBalance: PositiveAmount.T
   /// If the balance is below the lower bound or above the
   /// upper bound then it will be restored to the target balance.
   TargetBalanceRange: TargetBalanceRange option
}

module TargetBalanceRule =
   let computeTransfer
      (rule: TargetBalanceRule)
      (currBalance: PositiveAmount.T)
      : AutoTransfer option
      =
      let target = rule.TargetAccount
      let partner = rule.ManagingPartnerAccount

      let rangeSatisfied =
         match rule.TargetBalanceRange with
         | None -> true
         | Some range ->
            currBalance < range.LowerBound || currBalance > range.UpperBound

      if currBalance < rule.TargetAccountBalance && rangeSatisfied then
         Some {
            Amount = currBalance
            Sender = {
               Name = partner.Name
               AccountId = partner.AccountId
               OrgId = partner.OrgId
            }
            Recipient = {
               Name = target.Name
               AccountId = target.AccountId
               OrgId = target.OrgId
            }
         }
      elif currBalance > rule.TargetAccountBalance && rangeSatisfied then
         Some {
            Amount = currBalance
            Sender = {
               Name = target.Name
               AccountId = target.AccountId
               OrgId = target.OrgId
            }
            Recipient = {
               Name = partner.Name
               AccountId = partner.AccountId
               OrgId = partner.OrgId
            }
         }
      else
         None

[<RequireQualifiedAccess>]
type AutomaticTransferRule =
   | ZeroBalance of ZeroBalanceRule
   | PercentDistribution of PercentDistributionRule.T
   | TargetBalance of TargetBalanceRule

type AutoTransferDerivedFromRule = {
   Rule: AutomaticTransferRule
   Transfer: AutoTransfer
}

let requiresBalanceManagement
   (frequency: Frequency)
   (rule: AutomaticTransferRule)
   (currBalance: decimal)
   : AutoTransferDerivedFromRule list option
   =
   PositiveAmount.create currBalance
   |> Option.bind (fun balance ->
      match rule, frequency with
      | AutomaticTransferRule.ZeroBalance o, Frequency.PerTransaction ->
         Some [
            {
               Rule = rule
               Transfer = ZeroBalanceRule.computeTransfer o balance
            }
         ]
      | AutomaticTransferRule.TargetBalance r, Frequency.Schedule s when
         s = CronSchedule.Daily
         ->
         TargetBalanceRule.computeTransfer r balance
         |> Option.map (fun transfer -> [
            { Rule = rule; Transfer = transfer }
         ])
      | AutomaticTransferRule.PercentDistribution r, frequency when
         frequency = (PercentDistributionRule.get r).Frequency
         ->
         PercentDistributionRule.computeTransfer r balance
         |> List.map (fun t -> { Transfer = t; Rule = rule })
         |> Some
      | _ -> None)

let requiresPerTransactionBalanceManagement =
   requiresBalanceManagement Frequency.PerTransaction

let requiresDailyBalanceManagement =
   requiresBalanceManagement (Frequency.Schedule CronSchedule.Daily)

let requiresTwiceMonthlyBalanceManagement =
   requiresBalanceManagement (Frequency.Schedule CronSchedule.TwiceMonthly)

type AutomaticTransferConfig = {
   Id: Guid
   Info: AutomaticTransferRule
}

type AutomaticTransferRuleCount = {
   PerTransaction: int
   Daily: int
   TwiceMonthly: int
}

let autoTransferRuleCounts
   (rules: AutomaticTransferRule list)
   : AutomaticTransferRuleCount
   =
   List.fold
      (fun acc rule ->
         match rule with
         | AutomaticTransferRule.ZeroBalance _ -> {
            acc with
               PerTransaction = acc.PerTransaction + 1
           }
         | AutomaticTransferRule.TargetBalance _ -> {
            acc with
               Daily = acc.Daily + 1
           }
         | AutomaticTransferRule.PercentDistribution r ->
            match (PercentDistributionRule.get r).Frequency with
            | Frequency.PerTransaction -> {
               acc with
                  PerTransaction = acc.PerTransaction + 1
              }
            | Frequency.Schedule schedule ->
               match schedule with
               | CronSchedule.Daily -> { acc with Daily = acc.Daily + 1 }
               | CronSchedule.TwiceMonthly -> {
                  acc with
                     TwiceMonthly = acc.TwiceMonthly + 1
                 })
      {
         PerTransaction = 0
         Daily = 0
         TwiceMonthly = 0
      }
      rules

[<RequireQualifiedAccess>]
type Message = StartScheduledAutoTransfers of CronSchedule

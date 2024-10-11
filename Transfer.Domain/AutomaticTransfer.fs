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

   override x.ToString() =
      match x with
      | Frequency.PerTransaction -> "PerTransaction"
      | Frequency.Schedule CronSchedule.Daily -> "Daily"
      | Frequency.Schedule CronSchedule.TwiceMonthly -> "TwiceMonthly"

   static member fromString(input: string) : Frequency option =
      match input with
      | "PerTransaction" -> Some Frequency.PerTransaction
      | "Daily" -> Some(Frequency.Schedule CronSchedule.Daily)
      | "TwiceMonthly" -> Some(Frequency.Schedule CronSchedule.TwiceMonthly)
      | _ -> None

   member x.Display =
      match x with
      | Frequency.PerTransaction -> "Every transaction"
      | Frequency.Schedule CronSchedule.Daily -> "Every day at 8AM"
      | Frequency.Schedule CronSchedule.TwiceMonthly ->
         "Twice monthly on the 1st and 15th"

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

   [<RequireQualifiedAccess>]
   type ValidationError =
      | LessThan1Destination
      | TotalPercentAllocatedNot100 of decimal
      | ContainsNegativePercentAllocation
      | SenderIsDestination

      override x.ToString() =
         match x with
         | ValidationError.LessThan1Destination ->
            "Should have at least 1 destination."
         | ValidationError.TotalPercentAllocatedNot100 allocatedSum ->
            string allocatedSum
            + " of 100% allocated.  Total allocated must be equal to 100."
         | ValidationError.ContainsNegativePercentAllocation ->
            "Destinations should only contain positive percent allocations."
         | ValidationError.SenderIsDestination ->
            "A sender can not be included as a destination."

   let get (T value) = value

   let create
      (frequency: Frequency)
      (sender: InternalTransferSender)
      (unvalidatedDestination: UnvalidatedDistributionDestinationAccount list)
      : Result<T, ValidationError>
      =
      validate {
         let! _ =
            Check.List.greaterThanLen 0 "Destination" unvalidatedDestination
            |> Result.mapError (fun _ -> ValidationError.LessThan1Destination)

         let percentSum =
            unvalidatedDestination |> List.sumBy _.ProposedPercentAllocated

         let! _ =
            Check.Decimal.equals 100m "Percent allocated" percentSum
            |> Result.mapError (fun _ ->
               ValidationError.TotalPercentAllocatedNot100 percentSum)

         let senderIsNotARecipient =
            unvalidatedDestination
            |> List.forall (fun o -> o.Recipient.AccountId <> sender.AccountId)

         let! destination =
            if senderIsNotARecipient then
               Ok unvalidatedDestination
            else
               Error ValidationError.SenderIsDestination

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
               Error ValidationError.ContainsNegativePercentAllocation

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
      (currBalance: decimal)
      : AutoTransfer option
      =
      let target = rule.TargetAccount
      let partner = rule.ManagingPartnerAccount

      let rangeSatisfied =
         match rule.TargetBalanceRange with
         | None -> true
         | Some range ->
            currBalance < PositiveAmount.get range.LowerBound
            || currBalance > PositiveAmount.get range.UpperBound

      if
         rangeSatisfied
         && currBalance < (PositiveAmount.get rule.TargetAccountBalance)
      then
         let transferAmount =
            PositiveAmount.map
               (fun target -> target - currBalance)
               rule.TargetAccountBalance

         Some {
            Amount = transferAmount
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
      elif
         rangeSatisfied
         && currBalance > (PositiveAmount.get rule.TargetAccountBalance)
      then
         let transferAmount =
            PositiveAmount.map
               (fun target -> currBalance - target)
               rule.TargetAccountBalance

         Some {
            Amount = transferAmount
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

let computeTransfer
   (rule: AutomaticTransferRule)
   (currBalance: decimal)
   : AutoTransferDerivedFromRule list option
   =
   match rule with
   | AutomaticTransferRule.ZeroBalance r ->
      PositiveAmount.create currBalance
      |> Option.map (fun balance -> [
         {
            Rule = rule
            Transfer = ZeroBalanceRule.computeTransfer r balance
         }
      ])
   | AutomaticTransferRule.TargetBalance r ->
      TargetBalanceRule.computeTransfer r currBalance
      |> Option.map (fun transfer -> [ { Rule = rule; Transfer = transfer } ])
   | AutomaticTransferRule.PercentDistribution r ->
      PositiveAmount.create currBalance
      |> Option.map (fun balance ->
         PercentDistributionRule.computeTransfer r balance
         |> List.map (fun t -> { Transfer = t; Rule = rule }))

let requiresBalanceManagement
   (frequency: Frequency)
   (rule: AutomaticTransferRule)
   (currBalance: decimal)
   : AutoTransferDerivedFromRule list option
   =
   let requiresManagement =
      match rule, frequency with
      | AutomaticTransferRule.ZeroBalance _, Frequency.PerTransaction -> true
      | AutomaticTransferRule.TargetBalance _, Frequency.Schedule s ->
         s = CronSchedule.Daily
      | AutomaticTransferRule.PercentDistribution r, frequency ->
         frequency = (PercentDistributionRule.get r).Frequency
      | _ -> false

   if requiresManagement then
      computeTransfer rule currBalance
   else
      None

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

let frequencyFromAutoTransferRule =
   function
   | AutomaticTransferRule.ZeroBalance _ -> Frequency.PerTransaction
   | AutomaticTransferRule.TargetBalance _ ->
      Frequency.Schedule(CronSchedule.Daily)
   | AutomaticTransferRule.PercentDistribution r ->
      (PercentDistributionRule.get r).Frequency

module CycleDetection =
   type private Edge = {
      Sender: AccountId
      Recipients: AccountId list
   }

   let private getRecipients (sender: AccountId) (existingEdges: Edge list) =
      existingEdges
      |> List.collect (fun edge ->
         if edge.Sender = sender then edge.Recipients else [])

   let rec private dfs
      (existingEdges: Edge list)
      (visited: Set<AccountId>)
      (target: AccountId)
      (currRecipientId: AccountId)
      =
      if visited.Contains(currRecipientId) then
         false
      elif currRecipientId = target then
         true
      else
         let visited = visited.Add currRecipientId
         let neighbors = getRecipients currRecipientId existingEdges
         neighbors |> List.exists (dfs existingEdges visited target)

   let cycleDetected
      (ruleToAdd: AutomaticTransferRule)
      (existingRules: AutomaticTransferRule list)
      =
      let edges =
         existingRules
         |> List.collect (function
            | AutomaticTransferRule.ZeroBalance r -> [
               {
                  Recipients = [ r.Recipient.AccountId ]
                  Sender = r.Sender.AccountId
               }
              ]
            | AutomaticTransferRule.PercentDistribution r ->
               let r = PercentDistributionRule.get r

               [
                  {
                     Sender = r.Sender.AccountId
                     Recipients =
                        r.DestinationAccounts |> List.map _.Recipient.AccountId
                  }
               ]
            | AutomaticTransferRule.TargetBalance r ->
               let targetId = r.TargetAccount.AccountId
               let partnerId = r.ManagingPartnerAccount.AccountId

               [
                  {
                     Sender = targetId
                     Recipients = [ partnerId ]
                  }
                  {
                     Sender = partnerId
                     Recipients = [ targetId ]
                  }
               ])

      let dfs = dfs edges Set.empty

      match ruleToAdd with
      | AutomaticTransferRule.TargetBalance r ->
         let targetId = r.TargetAccount.AccountId
         let partnerId = r.ManagingPartnerAccount.AccountId
         dfs targetId partnerId || dfs partnerId targetId
      | AutomaticTransferRule.ZeroBalance r ->
         dfs r.Sender.AccountId r.Recipient.AccountId
      | AutomaticTransferRule.PercentDistribution r ->
         let r = PercentDistributionRule.get r

         r.DestinationAccounts
         |> List.exists (fun d -> dfs r.Sender.AccountId d.Recipient.AccountId)

[<RequireQualifiedAccess>]
type Message = StartScheduledAutoTransfers of CronSchedule

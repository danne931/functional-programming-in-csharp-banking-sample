module AutomaticBalanceManagementDashboard

open Feliz
open Feliz.Router
open Fable.FontAwesome
open System
open Browser.Types

open UIDomain.Account
open Bank.Account.Domain
open Bank.Employee.Domain
open Lib.SharedTypes
open AutomaticTransfer
open AutomaticTransferRule
open Bank.Account.Forms.ConfigureAutomaticTransferTargetBalanceRule
open Bank.Account.Forms.ConfigureAutomaticTransferZeroBalanceRule
open Bank.Account.Forms.ConfigureAutomaticTransferPercentDistributionRule
open LeaderLine

type Arrow = {
   StartId: AccountId
   EndId: AccountId
   RuleId: Guid
   Direction: ArrowDirection
}

module Arrow =
   let fromRuleConfig (config: AutomaticTransferConfig) : Arrow list =
      match config.Info with
      | AutomaticTransferRule.ZeroBalance r -> [
         {
            StartId = r.Sender.AccountId
            EndId = r.Recipient.AccountId
            RuleId = config.Id
            Direction = ArrowDirection.StartToEnd
         }
        ]
      | AutomaticTransferRule.TargetBalance r -> [
         {
            StartId = r.TargetAccount.AccountId
            EndId = r.ManagingPartnerAccount.AccountId
            RuleId = config.Id
            Direction = ArrowDirection.Bidirectional
         }
        ]
      | AutomaticTransferRule.PercentDistribution r ->
         let r = PercentDistributionRule.get r

         r.DestinationAccounts
         |> List.map (fun o -> {
            StartId = r.Sender.AccountId
            EndId = o.Recipient.AccountId
            RuleId = config.Id
            Direction = ArrowDirection.StartToEnd
         })

type ArrowLayout = {
   Nodes: Map<AccountId, Account>
   // One rule can have many edges (arrows)
   ArrowsPerRule: Map<Guid, Arrow list>
}

let computeArrowLayout (accounts: Map<AccountId, Account>) =
   let arrows =
      accounts.Values
      |> Seq.choose _.AutoTransferRule
      |> Seq.map Arrow.fromRuleConfig
      |> Seq.collect id
      |> Seq.toList

   let accountIsNode: Set<AccountId> =
      Seq.fold
         (fun acc arrow ->
            let acc = Set.add arrow.StartId acc
            Set.add arrow.EndId acc)
         Set.empty
         arrows

   let accountsAsNodes =
      accounts
      |> Map.filter (fun accountId _ -> accountIsNode.Contains accountId)

   let arrowsPerRule =
      List.fold
         (fun acc (conf: Arrow) ->
            acc
            |> Map.change conf.RuleId (fun edgesOpt ->
               match edgesOpt with
               | None -> Some [ conf ]
               | Some edges -> Some(conf :: edges)))
         Map.empty
         arrows

   {
      Nodes = accountsAsNodes
      ArrowsPerRule = arrowsPerRule
   }

[<ReactComponent>]
let AutomaticBalanceManagementDashboardComponent
   (session: UserSession)
   (accounts: Map<AccountId, Account>)
   (url: Routes.AccountUrl)
   =
   let orgProviderDispatch = React.useContext OrgProvider.dispatchContext

   let onRuleSave =
      fun (receipt: AccountCommandReceipt) ->
         OrgProvider.Msg.AccountUpdated {
            Account = receipt.PendingState
            EventPersisted = receipt.PendingEvent
            Date = DateTime.UtcNow
         }
         |> orgProviderDispatch

   let nodeRefs = React.useRef Map.empty<AccountId, Element>
   let arrowRefs = React.useRef Map.empty<Guid, ILeaderLine list>

   let arrowLayout = computeArrowLayout accounts

   let rules =
      List.fold
         (fun acc account ->
            match account.AutoTransferRule with
            | Some rule -> (account, rule) :: acc
            | None -> acc)
         []
         (Seq.toList accounts.Values)

   let canAddAnotherRule = rules.Length <> accounts.Count

   let arrowIter (fn: ILeaderLine -> unit) (ruleId: Guid) =
      arrowRefs.current |> Map.tryFind ruleId |> (Option.iter << List.iter) fn

   let createArrows () =
      for ruleId, arrows in Map.toSeq arrowLayout.ArrowsPerRule do
         let arrows =
            arrows
            |> List.choose (fun arrow ->
               Option.map2
                  (fun start finish ->
                     LeaderLine.create start finish arrow.Direction)
                  (Map.tryFind arrow.StartId nodeRefs.current)
                  (Map.tryFind arrow.EndId nodeRefs.current))

         arrowRefs.current <- Map.add ruleId arrows arrowRefs.current

   let removeArrows () =
      arrowRefs.current.Keys
      |> Seq.iter (fun ruleId ->
         arrowIter LeaderLine.remove ruleId
         arrowRefs.current <- Map.remove ruleId arrowRefs.current)

   // NOTE:
   // Currently no dependencies included in the dependency array
   // so there will be unnecessary removal and creation of arrows on
   // each render.  Though unnecessary, it appears there is no visual
   // or performance impact so will leave alone for now.
   //
   // Ideally we want the effect to only run when the arrow configuration
   // changes.  If we update the dependencies to do so then we see this
   // error from LeaderLine library code:
   // if (e.compareDocumentPosition(i) & Node.DOCUMENT_POSITION_DISCONNECTED)
   //    return console.error("A disconnected element was passed."),
   //
   // Reproduce:
   // 1. Use this as dependency array [| box (string arrowLayout.ArrowsPerRule) |]
   // 2. Click to edit an existing rule (opens the sidebar and updates the route)
   // 3. Close the edit
   // 4. Hover over a rule.  We would expect to see the corresponding arrow
   //    become animated but instead LeaderLine throws the above error.
   //
   // TODO:
   // It appears LeaderLine may lose it references to the arrow SVGs
   // (which exist outside of the React app DOM) when the route changes.
   // A potential fix may be to swap LeaderLine for LinkerLine
   // https://www.npmjs.com/package/linkerline which is built on top of
   // LeaderLine.  LinkerLine allows the SVG arrows to be attached to any
   // parent element so perhaps attaching them to a node in the React app,
   // such as this component may be the fix.
   React.useLayoutEffect (fun () ->
      removeArrows ()
      createArrows ()
      React.createDisposable removeArrows)

   classyNode Html.div [ "automatic-balance-management" ] [
      match url with
      | Routes.AccountUrl.CreateRule ruleRoute ->
         classyNode Html.article [
            "form-wrapper"
            "automatic-balance-create-rule"
         ] [
            Html.h6 (
               match ruleRoute with
               | Routes.CreateAutoTransferRuleUrl.ZeroBalance ->
                  "Create Zero Balance Rule"
               | Routes.CreateAutoTransferRuleUrl.TargetBalance ->
                  "Create Target Balance Rule"
               | Routes.CreateAutoTransferRuleUrl.PercentDistribution ->
                  "Create Percent Distribution Rule"
            )

            match ruleRoute with
            | Routes.CreateAutoTransferRuleUrl.ZeroBalance ->
               ConfigureAutoTransferZeroBalanceRuleFormComponent
                  onRuleSave
                  session
                  accounts
                  None
            | Routes.CreateAutoTransferRuleUrl.TargetBalance ->
               ConfigureAutoTransferTargetBalanceRuleFormComponent
                  onRuleSave
                  session
                  accounts
                  None
            | Routes.CreateAutoTransferRuleUrl.PercentDistribution ->
               ConfigureAutoTransferPercentDistributionRuleFormComponent
                  onRuleSave
                  session
                  accounts
                  None
         ]
         |> ScreenOverlay.Portal
      | Routes.AccountUrl.EditRule ruleId ->
         let ruleConfigOpt =
            accounts.Values
            |> Seq.toList
            |> List.tryPick (
               _.AutoTransferRule >> Option.filter (fun r -> r.Id = ruleId)
            )

         match ruleConfigOpt with
         | None ->
            classyNode Html.article [ "form-wrapper" ] [
               Html.p "Rule not found."
            ]
            |> ScreenOverlay.Portal
         | Some ruleConfig ->
            classyNode Html.article [
               "form-wrapper"
               "automatic-balance-create-rule"
            ] [
               Html.h6 (
                  match ruleConfig.Info with
                  | AutomaticTransferRule.ZeroBalance _ ->
                     "Edit Zero Balance Rule"
                  | AutomaticTransferRule.TargetBalance _ ->
                     "Edit Target Balance Rule"
                  | AutomaticTransferRule.PercentDistribution _ ->
                     "Edit Percent Distribution Rule"
               )

               match ruleConfig.Info with
               | AutomaticTransferRule.ZeroBalance rule ->
                  ConfigureAutoTransferZeroBalanceRuleFormComponent
                     onRuleSave
                     session
                     accounts
                     (Some(ruleConfig.Id, rule))
               | AutomaticTransferRule.TargetBalance rule ->
                  ConfigureAutoTransferTargetBalanceRuleFormComponent
                     onRuleSave
                     session
                     accounts
                     (Some(ruleConfig.Id, rule))
               | AutomaticTransferRule.PercentDistribution rule ->
                  ConfigureAutoTransferPercentDistributionRuleFormComponent
                     onRuleSave
                     session
                     accounts
                     (Some(ruleConfig.Id, rule))
            ]
            |> ScreenOverlay.Portal
      | _ -> ()

      Html.section [
         Html.h6 "Automatic Balance Management Rules"

         if arrowLayout.Nodes.IsEmpty then
            Html.p "No automatic balance management rules configured."
         else
            classyNode Html.div [ "grid"; "balance-management-container" ] [
               classyNode Html.div [ "grid"; "balance-management-nodes" ] [
                  for account in arrowLayout.Nodes.Values do
                     Html.div [
                        Html.article [
                           attr.classes [ "account-node" ]
                           attr.ref (fun el ->
                              nodeRefs.current <-
                                 Map.add account.AccountId el nodeRefs.current)

                           attr.children [
                              Html.b account.FullName
                              Html.small (Money.format account.Balance)
                           ]
                        ]
                     ]
               ]

               classyNode Html.div [ "balance-management-rules" ] [
                  for account, config in rules do
                     Html.article [
                        AutoTransferRuleComponent {|
                           Session = session
                           AutoTransferConfig = config
                           TargetAccount = account
                           onRuleDeleted = onRuleSave
                           OnMouseEnter =
                              fun () ->
                                 arrowIter
                                    (LeaderLine.animate >> ignore)
                                    config.Id
                           OnMouseLeave =
                              fun () ->
                                 arrowIter
                                    (LeaderLine.stopAnimation >> ignore)
                                    config.Id
                        |}
                     ]
               ]
            ]
      ]

      classyNode Html.section [ "configure-rule-options" ] [
         Html.h6 "Configure Rule"

         if (not canAddAnotherRule) then
            Html.small "All accounts are configured with a rule."

         classyNode Html.div [ "grid" ] [
            Html.button [
               attr.disabled (not canAddAnotherRule)
               attr.children [
                  Fa.i [ Fa.Solid.DotCircle ] []
                  Html.span "Keep a zero balance"
               ]

               attr.onClick (fun _ ->
                  Routes.AccountUrl.CreateZeroBalanceRulePath
                  |> Router.navigate)
            ]

            Html.p
               "Move money from one account to another
               automatically to maintain a zero balance."
         ]

         classyNode Html.div [ "grid" ] [
            Html.button [
               attr.disabled (not canAddAnotherRule)
               attr.children [
                  Fa.i [ Fa.Solid.ArrowsAltH ] []
                  Html.span "Maintain a target balance"
               ]

               attr.onClick (fun _ ->
                  Routes.AccountUrl.CreateTargetBalanceRulePath
                  |> Router.navigate)
            ]

            Html.p
               "Maintain a target balance for an account by moving
               money to and from a partner account."
         ]

         classyNode Html.div [ "grid" ] [
            Html.button [
               attr.disabled (not canAddAnotherRule)
               attr.children [
                  Fa.i [ Fa.Solid.Percent ] []
                  Html.span "Distribute by percentage"
               ]

               attr.onClick (fun _ ->
                  Routes.AccountUrl.CreatePercentDistributionRulePath
                  |> Router.navigate)
            ]

            Html.p
               "Distribute the balance of an account into multiple accounts
               by percentage."
         ]
      ]
   ]

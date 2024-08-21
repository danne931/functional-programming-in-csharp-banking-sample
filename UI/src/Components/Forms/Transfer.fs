module Bank.Account.Forms.TransferForm

open Feliz
open Feliz.Router
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.Employee.Domain
open UIDomain.Account
open Lib.Validators
open FormContainer
open Lib.SharedTypes

type Values = {
   Amount: string
   RecipientId: string
   Memo: string
}

let amountField (account: Account) =
   Form.textField {
      Parser =
         amountValidatorFromString "Transfer amount"
         >> validationErrorsHumanFriendly
         >> Result.bind (fun amt ->
            if account.Balance - amt < 0m then
               Result.Error $"Insufficient Balance ${account.Balance}"
            else
               Ok amt)
      Value = fun (values: Values) -> values.Amount
      Update = fun newValue values -> { values with Amount = newValue }
      Error = fun _ -> None
      Attributes = {
         Label = "Transfer Amount:"
         Placeholder = "25"
         HtmlAttributes = []
      }
   }

let memoField =
   Form.textField {
      Parser = Ok
      Value = fun (values: Values) -> values.Memo
      Update = fun newValue values -> { values with Memo = newValue }
      Error = fun _ -> None
      Attributes = {
         Label = "Memo:"
         Placeholder = "Reason for Transfer"
         HtmlAttributes = []
      }
   }

let memoForm = Form.succeed id |> Form.append memoField |> Form.optional

let formInternalWithinOrg
   (account: Account)
   (accountProfiles: Map<AccountId, AccountProfile>)
   (initiatedBy: InitiatedById)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let internalWithinOrgOptions =
      accountProfiles
      |> Map.toList
      |> List.filter (fun (acctId, _) -> acctId <> account.AccountId)
      |> List.map (fun (acctId, profile) ->
         string acctId, $"{profile.Name} ({Money.format profile.Balance})")
      |> List.sortBy snd

   let fieldInternalWithinOrgSelect =
      Form.selectField {
         Parser = Ok
         Value = fun values -> values.RecipientId
         Update = fun newValue values -> { values with RecipientId = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Move money to account:"
            Placeholder = "No account selected"
            Options = internalWithinOrgOptions
         }
      }

   let onSubmit (selectedId: string) (amount: decimal) =
      let profile = accountProfiles[selectedId |> Guid.Parse |> AccountId]

      let cmd =
         InternalTransferWithinOrgCommand.create account.CompositeId initiatedBy {
            BaseInfo = {
               ScheduledDate = DateTime.UtcNow
               Amount = amount
               RecipientOrgId = profile.OrgId
               RecipientId = profile.AccountId
               RecipientName = profile.Name
               Sender = {
                  Name = account.Name
                  AccountId = account.AccountId
                  OrgId = account.OrgId
               }
            }
         }
         |> AccountCommand.InternalTransfer

      Msg.Submit(account, cmd, Started)

   Form.succeed onSubmit
   |> Form.append fieldInternalWithinOrgSelect
   |> Form.append (amountField account)

let formInternalCrossOrg
   (account: Account)
   (orgs: Org list)
   (initiatedBy: InitiatedById)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let internalCrossOrgOptions =
      orgs
      |> List.choose (fun org ->
         org.Permissions.SocialTransferDiscoveryPrimaryAccountId
         |> Option.map (fun id -> string id, org.Name))
      |> List.sortBy snd

   let fieldInternalCrossOrgSelect =
      Form.selectField {
         Parser = Ok
         Value = fun values -> values.RecipientId
         Update = fun newValue values -> { values with RecipientId = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Transfer to organization:"
            Placeholder = "No organization selected"
            Options = internalCrossOrgOptions
         }
      }

   let onSubmit (selectedId: string) (amount: decimal) (memo: string option) =
      let memo =
         memo
         |> Option.bind (fun memo ->
            if String.IsNullOrWhiteSpace memo then None else Some memo)

      let org =
         orgs
         |> List.find (fun o ->
            string o.Permissions.SocialTransferDiscoveryPrimaryAccountId = selectedId)

      let cmd =
         InternalTransferBetweenOrgsCommand.create
            account.CompositeId
            initiatedBy
            {
               BaseInfo = {
                  ScheduledDate = DateTime.UtcNow
                  Amount = amount
                  RecipientOrgId = org.OrgId
                  RecipientId = selectedId |> Guid.Parse |> AccountId
                  RecipientName = org.Name
                  Sender = {
                     Name = account.Name
                     AccountId = account.AccountId
                     OrgId = account.OrgId
                  }
               }
               Memo = memo
            }
         |> AccountCommand.InternalTransferBetweenOrgs

      Msg.Submit(account, cmd, Started)

   Form.succeed onSubmit
   |> Form.append fieldInternalCrossOrgSelect
   |> Form.append (amountField account)
   |> Form.append memoForm

let formDomestic
   (account: Account)
   (initiatedBy: InitiatedById)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let domesticOptions =
      account.DomesticTransferRecipients
      |> Map.toList
      |> List.map (fun (recipientId, recipient) ->
         let name = recipient.Nickname |> Option.defaultValue recipient.Name
         string recipientId, $"{name} **{recipient.AccountNumber.Last4}")
      |> List.sortBy snd

   let fieldDomesticSelect =
      Form.selectField {
         Parser = Ok
         Value = fun values -> values.RecipientId
         Update = fun newValue values -> { values with RecipientId = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Domestic transfer recipient:"
            Placeholder = "No recipient selected"
            Options = domesticOptions
         }
      }

   let onSubmit (selectedId: string) (amount: decimal) (memo: string option) =
      let memo =
         memo
         |> Option.bind (fun memo ->
            if String.IsNullOrWhiteSpace memo then None else Some memo)

      let accountId = selectedId |> Guid.Parse |> AccountId
      let recipient = account.DomesticTransferRecipients[accountId]

      let cmd =
         DomesticTransferCommand.create account.CompositeId initiatedBy {
            ScheduledDate = DateTime.UtcNow
            Amount = amount
            Sender = {
               Name = account.Name
               AccountNumber = account.AccountNumber
               RoutingNumber = account.RoutingNumber
               OrgId = account.OrgId
               AccountId = account.AccountId
            }
            Recipient = recipient
            Memo = memo
         }
         |> AccountCommand.DomesticTransfer

      Msg.Submit(account, cmd, Started)

   Form.succeed onSubmit
   |> Form.append fieldDomesticSelect
   |> Form.append (amountField account)
   |> Form.append memoForm

[<ReactComponent>]
let TransferFormComponent
   (session: UserSession)
   (account: Account)
   (accountProfiles: Map<AccountId, AccountProfile>)
   (onSubmit: ParentOnSubmitHandler)
   =
   let initValues = {
      Amount = ""
      RecipientId = ""
      Memo = ""
   }

   let initiatedBy = InitiatedById session.EmployeeId

   let selectedAccountEnv, setSelectedAccountEnv =
      React.useState RecipientAccountEnvironment.InternalWithinOrg

   React.fragment [
      Html.select [
         attr.onChange (
            function
            | "Domestic" -> RecipientAccountEnvironment.Domestic
            | "InternalWithinOrg" ->
               RecipientAccountEnvironment.InternalWithinOrg
            | _ -> RecipientAccountEnvironment.InternalCrossOrg
            >> setSelectedAccountEnv
         )
         attr.value (string selectedAccountEnv)

         attr.children [
            Html.option [
               attr.value (string RecipientAccountEnvironment.InternalWithinOrg)
               attr.text "Internal transfer within organization"
            ]

            Html.option [
               attr.value (string RecipientAccountEnvironment.InternalCrossOrg)
               attr.text "Internal transfer to another organization"
            ]

            Html.option [
               attr.value (string RecipientAccountEnvironment.Domestic)
               attr.text "Domestic transfer"
            ]
         ]
      ]

      match selectedAccountEnv with
      | RecipientAccountEnvironment.InternalCrossOrg ->
         OrgSocialTransferDiscovery.OrgSearchComponent
            session.OrgId
            (fun searchInput orgs ->
               match orgs with
               | Deferred.InProgress -> Html.progress []
               | Deferred.Resolved(Ok(Some orgs)) ->
                  let initValues =
                     orgs
                     |> List.head
                     |> (fun o ->
                        o.Permissions.SocialTransferDiscoveryPrimaryAccountId)
                     |> Option.map (fun accountId -> {
                        initValues with
                           RecipientId = string accountId
                     })
                     |> Option.defaultValue initValues

                  AccountFormContainer
                     initValues
                     (formInternalCrossOrg account orgs initiatedBy)
                     onSubmit
               | Deferred.Resolved(Ok None) ->
                  Html.p $"No orgs found by search query {searchInput}."
               | _ -> Html.none)
      | RecipientAccountEnvironment.InternalWithinOrg ->
         AccountFormContainer
            initValues
            (formInternalWithinOrg account accountProfiles initiatedBy)
            onSubmit
      | RecipientAccountEnvironment.Domestic ->
         if account.DomesticTransferRecipients.Count = 0 then
            Html.button [
               attr.classes [ "outline" ]
               attr.text "No recipients.  Click here to create."
               attr.onClick (fun _ ->
                  let pathArr =
                     Routes.TransactionUrl.selectedPath account.AccountId

                  let queryString =
                     {
                        AccountBrowserQuery.empty with
                           Action =
                              Some AccountActionView.RegisterTransferRecipient
                     }
                     |> AccountBrowserQuery.toQueryParams
                     |> Router.encodeQueryString

                  Router.navigate [| yield! pathArr; queryString |])
            ]
         else
            AccountFormContainer
               initValues
               (formDomestic account initiatedBy)
               onSubmit
   ]

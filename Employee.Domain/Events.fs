namespace Bank.Employee.Domain

open Lib.SharedTypes

type CreatedAccountOwner = {
   Email: Email
   FirstName: string
   LastName: string
   InviteToken: InviteToken
}

type CreatedEmployee = {
   Role: Role
   Email: Email
   FirstName: string
   LastName: string
   OrgRequiresEmployeeInviteApproval: bool
   CardInfo: EmployeeInviteSupplementaryCardInfo option
}

type InvitationConfirmed = {
   Email: Email
   AuthProviderUserId: System.Guid
   Reference: string option
}

type InvitationApproved = {
   Email: Email
   InviteToken: InviteToken
   Approvers: EmployeeId list
}

type InvitationDenied = { Reason: string option }

type InvitationTokenRefreshed = {
   InviteToken: InviteToken
   OrgRequiresEmployeeInviteApproval: bool
   Reason: string option
}

type InvitationCancelled = { Reason: string option }

type CreatedCard = { Info: Card }

type DebitRequested = { Info: DebitInfo }

type DebitApproved = { Info: DebitInfo }

type DebitDeclined = { Info: DebitInfo }

type DailyDebitLimitUpdated = {
   CardId: CardId
   CardNumberLast4: int
   PriorLimit: decimal
   DebitLimit: decimal
}

type LockedCard = {
   CardId: CardId
   CardNumberLast4: int
   Reference: string option
}

type UnlockedCard = {
   CardId: CardId
   CardNumberLast4: int
   Reference: string option
}

type RoleUpdated = {
   Role: Role
   PriorRole: Role
   CardInfo: EmployeeInviteSupplementaryCardInfo option
}

type AccessRestored = { Reference: string option }

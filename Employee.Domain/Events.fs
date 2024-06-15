namespace Bank.Employee.Domain

open Lib.SharedTypes

type CreatedEmployee = {
   Role: EmployeeRole
   Email: Email
   FirstName: string
   LastName: string
}

type CreatedCard = { Info: Card }

type DebitRequested = { Info: DebitInfo }

type DebitApproved = { Info: DebitInfo }

type DebitDeclined = { Info: DebitInfo }

type DailyDebitLimitUpdated = { CardId: CardId; DebitLimit: decimal }

type LockedCard = {
   CardId: CardId
   Reference: string option
}

type UnlockedCard = {
   CardId: CardId
   Reference: string option
}

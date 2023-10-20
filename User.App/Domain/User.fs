module User

open System
open System.Threading.Tasks

open Lib.Types

type User = {
   FirstName: string
   LastName: string
   Email: Email
   AccountId: Guid
}

type UserDto = {
   FirstName: string
   LastName: string
   Email: string
   AccountId: Guid
}

let toDto (user: User) : UserDto = {
   FirstName = user.FirstName
   LastName = user.LastName
   Email = string user.Email
   AccountId = user.AccountId
}

type UserPersistence = {
   createUser: User -> Task<Result<int, Err>>
}

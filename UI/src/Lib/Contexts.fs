module Contexts

open Feliz

open Bank.Account.Domain

type SignalRContext = {
   Connection: SignalR.Connection option
   Errors: AccountEventRejected list
}

let signalRContext =
   React.createContext<SignalRContext> (
      name = "SignalRContext",
      defaultValue = { Connection = None; Errors = [] }
   )

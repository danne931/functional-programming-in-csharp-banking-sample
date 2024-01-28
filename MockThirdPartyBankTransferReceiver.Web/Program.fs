open Microsoft.AspNetCore.Builder
open Akkling
open Akkling.IO
open Akkling.IO.Tcp
open System
open System.Text.Json
open System.Net

let builder = WebApplication.CreateBuilder()
let app = builder.Build()

type Request = {
   Action: string
   AccountNumber: string
   RoutingNumber: string
   Amount: decimal
   Date: DateTime
   TransactionId: string
}

type Response = {
   accountNumber: string
   routingNumber: string
   ok: bool
   status: string
   reason: string
   transactionId: string
}

type InProgressTransfers = Map<string, Request>

let parseRequest (req: Request) (state: InProgressTransfers) =
   let res = {
      accountNumber = req.AccountNumber
      routingNumber = req.RoutingNumber
      ok = true
      status = ""
      reason = ""
      transactionId = req.TransactionId
   }

   if req.Action = "TransferRequest" then
      if
         String.IsNullOrEmpty req.AccountNumber
         || String.IsNullOrEmpty req.RoutingNumber
      then
         {
            res with
               ok = false
               reason = "InvalidAccountInfo"
         },
         state
      elif req.Amount <= 0m then
         {
            res with
               ok = false
               reason = "InvalidAmount"
         },
         state
      else
         { res with status = "Processing" }, Map.add res.transactionId req state
   elif req.Action = "ProgressCheck" then
      { res with status = "Complete" }, Map.remove res.transactionId state
   else
      {
         res with
            ok = false
            reason = "InvalidAction"
      },
      state

let serializeResponse (response: Response) =
   response |> JsonSerializer.Serialize |> ByteString.ofUtf8String

let actorSystem =
   System.create "thirdparty-bank" <| Configuration.defaultConfig ()

// Reads from the connection are delegated to this actor.
let tcpMessageHandler connection (ctx: Actor<obj>) =
   monitor ctx connection |> ignore

   let rec loop (state: InProgressTransfers) = actor {
      let! msg = ctx.Receive()

      match msg with
      | Received(data) ->
         let req = data |> string |> JsonSerializer.Deserialize<Request>
         printfn "Received request %A" req

         let res, newState = parseRequest req state

         ctx.Sender() <! TcpMessage.Write(serializeResponse res)
         return! loop newState
      | Terminated(_, _, _)
      | ConnectionClosed(_) ->
         printfn "<Disconnected>"
         return Stop
      | _ -> return Ignore
   }

   loop Map.empty

let port, ip =
   try
      int <| Environment.GetEnvironmentVariable "TCP_BIND_PORT",
      Dns.GetHostAddresses(Dns.GetHostName())[0]
   with _ ->
      5007, IPAddress.Loopback

let endpoint = IPEndPoint(ip, port)

let tcpConnectionListener ctx =
   IO.Tcp(ctx) <! TcpMessage.Bind(untyped ctx.Self, endpoint, 100)

   let rec loop () = actor {
      let! (msg: obj) = ctx.Receive()

      match msg with
      | Bound endpoint -> printfn $"<Bound> \nEndpoint {endpoint}"
      | Connected(remote, _) ->
         let conn = ctx.Sender()

         printfn
            "<Connected> \nRemote Address: %A\nSender Actor Path: %A"
            remote
            conn.Path

         let aref = spawn ctx null (props (tcpMessageHandler conn))
         conn <! TcpMessage.Register(untyped aref)

         return! loop ()
      | _ -> return Ignore
   }

   loop ()

let listener =
   spawn actorSystem "tcpConnectionListener" (props tcpConnectionListener)

app.Run()

type Program() =
   class
   end

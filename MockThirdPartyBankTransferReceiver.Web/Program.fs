open Microsoft.AspNetCore.Builder
open Akkling
open Akkling.IO
open Akkling.IO.Tcp
open System
open System.Text.Json
open System.Net
open System.Collections.Generic

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

type InProgressTransfers = Dictionary<string, int * Request>

let inMemoryState = InProgressTransfers()

// Compute response & mutate in-memory state of in-progress transfers.
let processRequest (req: Request) =
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
         }
      elif req.Amount <= 0m then
         {
            res with
               ok = false
               reason = "InvalidAmount"
         }
      else
         inMemoryState.Add(res.transactionId, (0, req))
         { res with status = "ReceivedRequest" }
   elif req.Action = "ProgressCheck" then
      if not <| inMemoryState.ContainsKey(res.transactionId) then
         {
            res with
               ok = false
               reason = "NoTransferProcessing"
         }
      else
         let progressCheckCount, request = inMemoryState[res.transactionId]

         if progressCheckCount < 1 then
            inMemoryState[res.transactionId] <- progressCheckCount + 1, request

            {
               res with
                  status = "VerifyingAccountInfo"
            }
         else
            inMemoryState.Remove(res.transactionId) |> ignore
            { res with status = "Complete" }
   else
      {
         res with
            ok = false
            reason = "InvalidAction"
      }

let serializeResponse (response: Response) =
   response |> JsonSerializer.Serialize |> ByteString.ofUtf8String

let actorSystem =
   System.create "thirdparty-bank" <| Configuration.defaultConfig ()

// Reads from the connection are delegated to this actor.
let tcpMessageHandler connection (ctx: Actor<obj>) =
   monitor ctx connection |> ignore

   let rec loop () = actor {
      let! msg = ctx.Receive()

      match msg with
      | Received(data) ->
         let req = data |> string |> JsonSerializer.Deserialize<Request>
         printfn "Received request %A" req

         let res = processRequest req

         ctx.Sender() <! TcpMessage.Write(serializeResponse res)
         return! loop ()
      | Terminated(_, _, _)
      | ConnectionClosed(_) ->
         printfn "<Disconnected>"
         return Stop
      | _ -> return Ignore
   }

   loop ()

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

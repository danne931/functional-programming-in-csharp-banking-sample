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
   CorrelationId: string
}

type Response = {
   AccountNumber: string
   RoutingNumber: string
   Ok: bool
   Reason: string
   AckReceipt: string
}

let parseRequest (req: Request) =
   let res = {
      AccountNumber = req.AccountNumber
      RoutingNumber = req.RoutingNumber
      Ok = true
      Reason = ""
      AckReceipt = ""
   }

   if req.Action <> "TransferRequest" then
      {
         res with
            Ok = false
            Reason = "InvalidRequestAction"
      }
   elif
      String.IsNullOrEmpty req.AccountNumber
      || String.IsNullOrEmpty req.RoutingNumber
   then
      {
         res with
            Ok = false
            Reason = "InvalidAccount"
      }
   elif req.Amount <= 0m then
      {
         res with
            Ok = false
            Reason = "InvalidAmount"
      }
   else
      {
         res with
            AckReceipt = Guid.NewGuid() |> string
      }

let actorSystem =
   System.create "thirdparty-bank" <| Configuration.defaultConfig ()

// Reads from the connection are delegated to this actor.
let tcpMessageHandler connection (ctx: Actor<obj>) =
   monitor ctx connection |> ignore

   let rec loop () = actor {
      let! msg = ctx.Receive()

      match msg with
      | Received(data) ->
         let req = (string data)
         printfn "Received request %A" req

         let reply =
            req
            |> JsonSerializer.Deserialize<Request>
            |> parseRequest
            |> JsonSerializer.Serialize
            |> ByteString.ofUtf8String

         ctx.Sender() <! TcpMessage.Write reply
         return! loop ()
      | Terminated(_, _, _)
      | ConnectionClosed(_) ->
         printfn "<Disconnected>"
         return Stop
      | _ -> return Ignore
   }

   loop ()

let endpoint = IPEndPoint(IPAddress.Loopback, 5001)

let tcpConnectionListener ctx =
   IO.Tcp(ctx) <! TcpMessage.Bind(untyped ctx.Self, endpoint, 100)

   let rec loop () = actor {
      let! (msg: obj) = ctx.Receive()

      match msg with
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

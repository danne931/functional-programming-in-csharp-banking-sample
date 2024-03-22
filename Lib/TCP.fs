[<RequireQualifiedAccess>]
module TCP

open System
open System.Threading
open System.Threading.Tasks
open System.Text
open System.Net
open System.Net.Sockets

open Lib.SharedTypes

let request
   (host: IPAddress)
   (port: int)
   (encoding: Encoding)
   (msg: byte[])
   : Task<Result<string, Err>>
   =
   task {
      use client = new TcpClient()

      try
         do!
            client
               .ConnectAsync(IPEndPoint(host, port))
               .WaitAsync(TimeSpan.FromSeconds 3)

         use stream = client.GetStream()

         let ctsWrite = new CancellationTokenSource(TimeSpan.FromSeconds(1))
         do! stream.WriteAsync(msg, ctsWrite.Token)

         let buffer: byte[] = Array.zeroCreate 1024
         let ctsRead = new CancellationTokenSource(TimeSpan.FromSeconds(4))
         let! received = stream.ReadAsync(buffer, ctsRead.Token)

         return Ok(encoding.GetString(buffer, 0, received))
      with err ->
         return Error(Err.NetworkError err)
   }

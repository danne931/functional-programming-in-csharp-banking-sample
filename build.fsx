#!/usr/bin/env -S dotnet fsi
#r "nuget: Fake.Core.Target"

// Workaround for FAKE not yet supporting Dotnet 7
System.Environment.GetCommandLineArgs()
|> Array.skip 2
|> Array.toList
|> Fake.Core.Context.FakeExecutionContext.Create false __SOURCE_FILE__
|> Fake.Core.Context.RuntimeContext.Fake
|> Fake.Core.Context.setExecutionContext

open System
open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO
open Globbing.Operators

let projects = !! "**/*Web.fsproj" ++ "**/*.Service.fsproj"
let appEntryDir = "./Web"
let testDir = "./Test"

let publishProject project =
   Trace.trace $"Publishing project {project}..."
   Shell.Exec("dotnet", $"publish {project}") |> ignore

let dockerImageNameFromProject (fsproj: string) =
   match IO.Path.GetFileNameWithoutExtension fsproj with
   | "Web" -> Some "web"
   | "Account.Service" -> Some "account"
   | "Scheduler.Service" -> Some "scheduler"
   | "MockThirdPartyBankTransferReceiver.Web" ->
      Some "mock-third-party-bank-transfer-receiver"
   | _ -> None

let buildImage fsprojPath =
   let imageNameOpt = dockerImageNameFromProject fsprojPath

   match imageNameOpt with
   | None ->
      failwithf "Docker image name not specified for project %s" fsprojPath
   | Some imageName ->
      Trace.trace $"Building docker image {imageName}"
      let dirName = IO.Path.GetDirectoryName fsprojPath

      Shell.Exec("docker", $"build -t {imageName}:latest {dirName}") |> ignore

Target.create "Clean" (fun _ ->
   let dirs = !! "**/bin/" ++ "**/obj/"
   Trace.trace $"Cleaning directories: {dirs}"
   Shell.cleanDirs dirs
   Shell.rm $"{appEntryDir}/logs.json")

Target.create "Publish" (fun o ->
   let paths = o.Context.Arguments

   if paths.IsEmpty then
      Seq.iter publishProject projects
   else
      List.iter publishProject paths)

Target.create "BuildDockerImages" (fun o ->
   let paths = o.Context.Arguments

   if paths.IsEmpty then
      Seq.iter buildImage projects
   else
      List.iter buildImage paths)

Target.create "BuildApp" (fun _ ->
   Shell.Exec("dotnet", "build", dir = appEntryDir) |> ignore)

Target.create "RunApp" (fun _ ->
   Shell.Exec("dotnet", "tool restore") |> ignore
   Shell.Exec("dotnet", "watch", dir = appEntryDir) |> ignore)

// no-spinner option fixes intermittent hanging of Expecto
Target.create "Test" (fun _ ->
   Shell.Exec("dotnet", "run --no-spinner", dir = testDir) |> ignore)

"Clean" ==> "BuildApp"

"Clean" ==> "Publish" ==> "BuildDockerImages"

// start build
Target.runOrDefaultWithArguments "RunApp"

// NOTE:
// RunApp: sh build.sh
// Test: sh build.sh -t Test
// Build Docker images for docker compose: sh build.sh -t BuildDockerImages

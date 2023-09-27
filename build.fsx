#!/usr/bin/env -S dotnet fsi
#r "nuget: Fake.Core.Target"

// Workaround for FAKE not yet supporting Dotnet 7
System.Environment.GetCommandLineArgs()
|> Array.skip 2
|> Array.toList
|> Fake.Core.Context.FakeExecutionContext.Create false __SOURCE_FILE__
|> Fake.Core.Context.RuntimeContext.Fake
|> Fake.Core.Context.setExecutionContext

open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO

let cwd = "./AkkaIntegrated"
let testDir = "./Test"

Target.create "CleanTest" (fun _ ->
   let dirs = [ $"{testDir}/bin/"; $"{testDir}/obj/" ]
   Trace.trace $"Cleaning directories: {dirs}"
   Shell.cleanDirs dirs)

Target.create "CleanApp" (fun _ ->
   let dirs = [ $"{cwd}/bin/"; $"{cwd}/obj/" ]
   Trace.trace $"Cleaning directories: {dirs}"
   Shell.cleanDirs dirs
   Shell.rm $"{cwd}/logs.json")

Target.create "BuildApp" (fun _ ->
   Shell.Exec("dotnet", "build", dir = cwd) |> ignore)

Target.create "RunApp" (fun _ ->
   Shell.Exec("dotnet", "watch", dir = cwd) |> ignore)

// no-spinner option fixes intermittent hanging of Expecto
Target.create "Test" (fun _ ->
   Shell.Exec("dotnet", "run --no-spinner", dir = testDir) |> ignore)

"CleanApp" ==> "BuildApp"

// start build
Target.runOrDefaultWithArguments "RunApp"

// NOTE:
// RunApp: sh build.sh
// Test: sh build.sh -t Test
// BuildApp: sh build.sh -t BuildApp

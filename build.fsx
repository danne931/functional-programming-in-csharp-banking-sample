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
open Globbing.Operators

let appEntryDir = "./Web"
let testDir = "./Test"

Target.create "Clean" (fun _ ->
   let dirs = !! "**/bin/" ++ "**/obj/"
   Trace.trace $"Cleaning directories: {dirs}"
   Shell.cleanDirs dirs
   Shell.rm $"{appEntryDir}/logs.json")

Target.create "BuildApp" (fun _ ->
   Shell.Exec("dotnet", "build", dir = appEntryDir) |> ignore)

Target.create "RunApp" (fun _ ->
   Shell.Exec("dotnet", "watch", dir = appEntryDir) |> ignore)

// no-spinner option fixes intermittent hanging of Expecto
Target.create "Test" (fun _ ->
   Shell.Exec("dotnet", "run --no-spinner", dir = testDir) |> ignore)

"Clean" ==> "BuildApp"

// start build
Target.runOrDefaultWithArguments "RunApp"

// NOTE:
// RunApp: sh build.sh
// Test: sh build.sh -t Test
// BuildApp: sh build.sh -t BuildApp

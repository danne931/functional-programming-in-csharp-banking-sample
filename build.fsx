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

type ImageName = string
type DirectoryName = string

type ImageBuilder = {
   Program: string
   GetArgs: ImageName -> DirectoryName -> string
}

let imageBuilders = {|
   docker = {
      Program = "docker"
      GetArgs =
         fun imageName dirName -> $"build -t {imageName}:latest {dirName}"
   }
   minikube = {
      Program = "minikube"
      GetArgs =
         fun imageName dirName -> $"image build -t {imageName}:latest {dirName}"
   }
|}

let publishProject project =
   Trace.trace $"Publishing project {project}..."
   Shell.Exec("dotnet", $"publish {project}") |> ignore

let dockerImageNameFromProject (fsproj: string) =
   match IO.Path.GetFileNameWithoutExtension fsproj with
   | "Web" -> Some "web"
   | "Account.Service" -> Some "account"
   | "Scheduler.Service" -> Some "scheduler"
   | "MockThirdPartyBankTransferReceiver.Web" -> Some "mock-third-party-bank"
   | _ -> None

let buildImage (builder: ImageBuilder) fsprojPath =
   let imageNameOpt = dockerImageNameFromProject fsprojPath

   match imageNameOpt with
   | None ->
      failwithf "Docker image name not specified for project %s" fsprojPath
   | Some imageName ->
      Trace.trace $"Building docker image {imageName}"
      let dirName = IO.Path.GetDirectoryName fsprojPath

      Shell.Exec(builder.Program, builder.GetArgs imageName dirName) |> ignore

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
      Seq.iter (buildImage imageBuilders.docker) projects
   else
      List.iter (buildImage imageBuilders.docker) paths)

Target.create "RunDockerApp" (fun _ ->
   Shell.Exec("docker", "compose up") |> ignore)

Target.create "StartK8s" (fun _ -> Shell.Exec("minikube", "start") |> ignore)

Target.create "BuildDockerImagesForK8s" (fun o ->
   let paths = o.Context.Arguments

   if paths.IsEmpty then
      Seq.iter (buildImage imageBuilders.minikube) projects
   else
      List.iter (buildImage imageBuilders.minikube) paths)

Target.create "ApplyK8sResources" (fun _ ->
   Shell.Exec("minikube", "kubectl -- create namespace akkabank") |> ignore

   Shell.Exec(
      "minikube",
      "kubectl -- config set-context --current --namespace=akkabank"
   )
   |> ignore

   // ConfigMap of .sql scripts to set up postgres tables
   Shell.Exec(
      "minikube",
      "kubectl -- create configmap postgres-schemas --from-file=Infrastructure/Migrations"
   )
   |> ignore

   Shell.Exec(
      "helm",
      "install pg oci://registry-1.docker.io/bitnamicharts/postgresql --values=K8s/environment/values/postgres-values.yaml"
   )
   |> ignore

   let applyK8sResource resource =
      Shell.Exec("minikube", $"kubectl -- apply -f {resource}") |> ignore

   let resources = !! "./K8s/**/*.yaml" -- "./K8s/environment/values/*.yaml"
   Seq.iter applyK8sResource resources)

Target.create "RunK8sApp" (fun _ ->
   Shell.Exec(
      "minikube",
      "kubectl -- wait --for=condition=ready pod -l app=web-cluster --timeout=120s"
   )
   |> ignore

   Shell.Exec("minikube", "kubectl -- get all") |> ignore
   // Start tunnel for service web-cluster-http & open app in browser
   Shell.Exec("minikube", "service web-cluster-http -n akkabank") |> ignore)

Target.create "BuildApp" (fun _ ->
   Shell.Exec("dotnet", "build", dir = appEntryDir) |> ignore)

Target.create "RunApp" (fun _ ->
   Shell.Exec("dotnet", "tool restore") |> ignore
   Shell.Exec("dotnet", "watch", dir = appEntryDir) |> ignore)

// no-spinner option fixes intermittent hanging of Expecto
Target.create "Test" (fun _ ->
   Shell.Exec("dotnet", "run --no-spinner", dir = testDir) |> ignore)

"Clean" ==> "BuildApp"

"Clean" ==> "Publish" ==> "BuildDockerImages" ==> "RunDockerApp"

"Clean"
==> "Publish"
==> "StartK8s"
==> "BuildDockerImagesForK8s"
==> "ApplyK8sResources"
==> "RunK8sApp"

Target.runOrDefaultWithArguments "RunApp"

// NOTE:
// Run app: sh build.sh
// Run app via docker compose: sh build.sh -t RunDockerApp
// Run app via kubernetes: sh build.sh -t RunK8sApp
// Test: sh build.sh -t Test

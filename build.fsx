#!/usr/bin/env -S dotnet fsi
#r "nuget: Fake.Core.Target"

// Workaround for FAKE not yet supporting Dotnet versions > 6
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

type Env = { Pulumi: string; Dotnet: string }

let envs = {|
   Local = {
      Pulumi = "local"
      Dotnet = "Development"
   }
   Staging = {
      Pulumi = "staging"
      Dotnet = "Staging"
   }
   Production = {
      Pulumi = "production"
      Dotnet = "Production"
   }
|}

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
   let dirs = !! "**/bin/" ++ "**/obj/" ++ "./UI/dist/" ++ "./UI/node_modules/"
   Trace.trace $"Cleaning directories: {dirs}"
   Shell.cleanDirs dirs
   Shell.rm $"{appEntryDir}/logs.json")

Target.create "BuildUI" (fun _ ->
   Shell.cd "./UI/"

   let exitCode = Shell.Exec("npm", "install")

   if exitCode <> 0 then
      failwith "Error running npm install"

   let exitCode = Shell.Exec("npm", "run build")

   if exitCode <> 0 then
      failwith "Error running npm build"

   Shell.cd "../"

   Shell.copyDir "./Web/wwwroot/" "./UI/dist" (fun _ -> true))

Target.create "Publish" (fun o ->
   let paths = o.Context.Arguments

   let sources =
      if paths.IsEmpty then
         Seq.toArray projects
      else
         List.toArray paths

   Array.iter publishProject sources)

Target.create "BuildDockerImages" (fun o ->
   let paths = o.Context.Arguments

   let sources =
      if paths.IsEmpty then
         Seq.toArray projects
      else
         List.toArray paths

   Array.Parallel.iter (buildImage imageBuilders.docker) sources)

// NOTE: Pushes app images to public docker hub repos.
//       Currently pulling public app images into Azure Staging environment.
// TODO: Research Azure Container Registry & versioning docker images.
let pushImageToDockerHub (image: string) =
   let tag = $"danne931/akka-dotnet-bank-{image}"
   Trace.trace $"Uploading {tag} to public docker hub"

   let exitCode = Shell.Exec("docker", $"tag {image} {tag}")

   if exitCode <> 0 then
      Trace.traceError $"Error creating docker tag {tag}"

   let exitCode = Shell.Exec("docker", $"push {tag}")

   if exitCode <> 0 then
      Trace.traceError $"Error uploading {tag} to public docker hub"

Target.create "DockerHubPublic" (fun _ ->
   projects
   |> Seq.toArray
   |> Array.Parallel.iter (
      dockerImageNameFromProject >> Option.iter pushImageToDockerHub
   ))

Target.create "RunDockerApp" (fun _ ->
   Shell.Exec("docker", "compose up") |> ignore)

Target.create "StartK8s" (fun _ ->
   //Shell.Exec("minikube", "start --cpus 4 --memory 8192") |> ignore)
   Shell.Exec("minikube", "start --alsologtostderr -v=2") |> ignore

   Shell.Exec(
      "minikube",
      "kubectl -- config set-context minikube --namespace akkabank"
   )
   |> ignore)

Target.create "BuildDockerImagesForK8s" (fun o ->
   let paths = o.Context.Arguments

   let sources =
      if paths.IsEmpty then
         Seq.toArray projects
      else
         List.toArray paths

   Array.Parallel.iter (buildImage imageBuilders.minikube) sources)

Target.create "VerifyPulumiLogin" (fun _ ->
   if Shell.Exec("pulumi", "whoami") <> 0 then
      failwith
         "\n\nLogin to pulumi via command line before proceeding: pulumi login")

Target.create "VerifyAzureLogin" (fun _ ->
   let azureLoginResult = Shell.Exec("az", "login")

   if azureLoginResult <> 0 then
      failwith "Issue logging in to Azure.")

Target.create "ApplyK8sResources" (fun _ ->
   let env = envs.Local
   let pulumiStack = $"k8s-{env.Pulumi}"

   Shell.cd "Deploy/K8s"
   Shell.Exec("pulumi", $"stack init {pulumiStack}") |> ignore

   let selectResult = Shell.Exec("pulumi", $"stack select {pulumiStack}")

   if selectResult <> 0 then
      failwith $"Could not select {pulumiStack} stack"

   Shell.Exec("pulumi", $"config set environment {env.Dotnet}") |> ignore
   Shell.Exec("pulumi", "config set defaultK8Namespace akkabank") |> ignore
   Shell.Exec("pulumi", "config set postgresDatabase akkabank") |> ignore
   Shell.Exec("pulumi", "config set postgresUser testuser") |> ignore

   Shell.Exec("pulumi", "config set --secret postgresPassword testpass")
   |> ignore

   let result = Shell.Exec("pulumi", "up --skip-preview")

   if result <> 0 then
      failwith "Error applying K8s resources to minikube cluster")

let openK8sAppInBrowser () =
   Shell.Exec("minikube", "kubectl -- get all") |> ignore

   Shell.Exec(
      "minikube",
      "kubectl -- wait --for=condition=ready pod -l app=web-cluster --timeout=160s"
   )
   |> ignore

   Shell.Exec("minikube", "kubectl -- get all") |> ignore

   // Start tunnel for service web-cluster-http & open app in browser
   Shell.Exec("minikube", "service web-cluster-http -n akkabank") |> ignore

Target.create "RunK8sApp" (fun _ -> openK8sAppInBrowser ())

// no-spinner option fixes intermittent hanging of Expecto
Target.create "Test" (fun _ ->
   Shell.Exec("dotnet", "run --no-spinner", dir = testDir) |> ignore)

let pulumiAzure (env: Env) =
   let pulumiStack = $"azure-{env.Pulumi}"
   Trace.trace $"Deploying Azure resources to Pulumi stack {pulumiStack}"
   Shell.cd "Deploy/Azure"

   Shell.Exec("pulumi", $"stack init {pulumiStack}") |> ignore

   let selectResult = Shell.Exec("pulumi", $"stack select {pulumiStack}")

   if selectResult <> 0 then
      failwith $"Could not select {pulumiStack} stack"

   let azureDeployResult = Shell.Exec("pulumi", "up --skip-preview")

   if azureDeployResult <> 0 then
      failwith "Error deploying Azure resources"

   Shell.cd "../../"

let pulumiK8s (env: Env) =
   let pulumiStack = $"k8s-{env.Pulumi}"
   Trace.trace $"Deploying K8s resources to Pulumi stack {pulumiStack}"
   Shell.cd "Deploy/K8s"

   Shell.Exec("pulumi", $"stack init {pulumiStack}") |> ignore

   let selectResult = Shell.Exec("pulumi", $"stack select {pulumiStack}")

   if selectResult <> 0 then
      failwith $"Could not select {pulumiStack} stack"

   let whoAmI =
      CreateProcess.fromRawCommand "pulumi" [ "whoami" ]
      |> CreateProcess.redirectOutput
      |> Proc.run

   if whoAmI.ExitCode <> 0 then
      failwith "Could not determine current logged-in Pulumi user"

   let org = whoAmI.Result.Output |> String.removeLineBreaks
   let qualified = $"{org}/{env.Pulumi}"

   Shell.Exec("pulumi", $"env init {qualified}") |> ignore

   Shell.Exec(
      "pulumi",
      $"env set {qualified} pulumiConfig.environment {env.Dotnet}"
   )
   |> ignore

   Shell.Exec(
      "pulumi",
      $"env set {qualified} pulumiConfig.defaultK8Namespace akkabank"
   )
   |> ignore

   Shell.Exec(
      "pulumi",
      $"env set {qualified} pulumiConfig.postgresDatabase akkabank"
   )
   |> ignore

   Shell.Exec(
      "pulumi",
      $"env set {qualified} pulumiConfig.postgresUser testuser"
   )
   |> ignore

   Shell.Exec(
      "pulumi",
      $"env set {qualified} pulumiConfig.postgresPassword testpass --secret"
   )
   |> ignore

   let k8sDeployResult = Shell.Exec("pulumi", "up --skip-preview")

   if k8sDeployResult <> 0 then
      failwith "Error deploying K8s resources to AKS"

   Shell.cd "../../"

Target.create "DeployAzureStaging" (fun _ -> pulumiAzure envs.Staging)

Target.create "DeployK8sStaging" (fun _ -> pulumiK8s envs.Staging)

Target.create "DeployAllStaging" (fun _ ->
   pulumiAzure envs.Staging
   pulumiK8s envs.Staging
   Trace.trace "Finished deploying")

"Clean" ==> "BuildUI" ==> "Publish"

"Publish" ==> "BuildDockerImages"

"BuildDockerImages" ==> "RunDockerApp"

"Publish" ==> "StartK8s" ==> "BuildDockerImagesForK8s"

"VerifyPulumiLogin" ?=> "Clean"

"VerifyPulumiLogin" ==> "ApplyK8sResources"

"BuildDockerImagesForK8s" ==> "ApplyK8sResources"

"ApplyK8sResources" ==> "RunK8sApp"

// NOTE:
// Currently hosting images in public Docker Hub repos for Azure
// staging deployment to retrieve.
// TODO: Host containers in private Azure Container Registry.
"BuildDockerImages" ==> "DockerHubPublic"

"VerifyAzureLogin" ==> "DeployAllStaging"

"VerifyPulumiLogin" ==> "DeployAllStaging"

Target.runOrDefaultWithArguments "Clean"

// NOTE:
// Run app via docker compose: sh build.sh -t RunDockerApp
// Run app via kubernetes minikube: sh build.sh -t RunK8sApp
// Deploy app to Azure AKS staging environment: sh build.sh -t DeployAllStaging
//
// Selectively rebuild images for docker compose:
// sh build.sh -t BuildDockerImages ./Web/Web.fsproj ./Scheduler.Service/Scheduler.Service.fsproj
//
// Test: sh build.sh -t Test

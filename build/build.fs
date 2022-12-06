open System
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.Tools.Git
open MAB.DotIgnore

module private Path =
  let root = __SOURCE_DIRECTORY__ </> ".."
  let ignored = IgnoreList (root </> ".gitignore")

module private DotNet =
  let install = lazy DotNet.install DotNet.Versions.FromGlobalJson

module private Paket =
  let files = Path.root </> "paket-files"

  let restore () =
    Paket.restore (fun p ->
      { p with
          ToolType = ToolType.CreateLocalTool DotNet.install.Value
      })

module private Ninja =
  let exe =
    lazy
      (let binary =
        match Runtime.Host.OS with
        | Runtime.Windows -> "ninja.exe"

       Paket.files </> "**" </> binary
       |> GlobbingPattern.create
       |> Seq.exactlyOne)

module private Msvc =
  open BlackFox.VsWhere

  /// The requested version of MSVC
  [<Literal>]
  let Version = "14.34"

  /// Gets the MSVC environment variables from [vcvarsall.bat](https://learn.microsoft.com/en-us/cpp/build/building-on-the-command-line?view=msvc-170#developer_command_file_locations).
  let vcvars hostArchitecture targetArchitecture =

    // https://devblogs.microsoft.com/cppblog/finding-the-visual-c-compiler-tools-in-visual-studio-2017/#c-installation-workloads-and-components
    let components = "Microsoft.VisualStudio.Component.VC.Tools.x86.x64"

    let vsvars =
      VsInstances.getWithPackage components false
      |> List.sortByDescending (fun i -> i.InstallationVersion)
      |> List.tryHead
      |> function
        | Some i ->
          Path.combine i.InstallationPath "VC/Auxiliary/Build/vcvarsall.bat"
        | None -> invalidOp $"MSVC installation could not be found"

    /// Determines the 'arch' argument for vsvarsall from a host and target architecture.
    let arch host target =
      match host, target with
      | Runtime.X64, Runtime.X64 -> "amd64"
      | Runtime.X64, Runtime.X86 -> "amd64_x86"
      | Runtime.X86, Runtime.X86 -> "x86"
      | Runtime.X86, Runtime.X64 -> "x86_amd64"

    let parse (r : ProcessOutput) : Map<string, string> =
      r.Output
      |> String.splitStr Environment.NewLine
      |> List.skipWhile (fun line ->
        not <| line.StartsWith "[vcvarsall.bat] Environment initialized")
      |> List.skip 1
      |> List.map (fun line -> line.Split '=')
      |> List.takeWhile (fun parts -> parts.Length = 2)
      |> List.map (fun parts -> parts[0], parts[1])
      |> Map

    CreateProcess.fromRawCommandLine
      "cmd"
      $"""/c "{vsvars}" {arch hostArchitecture targetArchitecture} -vcvars_ver={Version} && set """
    |> CreateProcess.ensureExitCode
    |> CreateProcess.redirectOutput
    |> CreateProcess.mapResult parse
    |> Proc.run
    |> fun x -> x.Result

module private CMake =
  open Fake.Build

  let exe =
    lazy
      (let binary =
        match Runtime.Host.OS, Runtime.Host.Architecture with
        | Runtime.Windows, Runtime.X86 -> "windows-i386/bin/cmake.exe"
        | Runtime.Windows, Runtime.X64 -> "windows-x86_64/bin/cmake.exe"

       Paket.files </> "**" </> $"cmake-*-{binary}"
       |> GlobbingPattern.create
       |> Seq.exactlyOne)

  let source = Path.root </> "ext" </> "whisper.cpp"

  let private getToolchain (targetPlatform : Runtime.Platform) =
    match Runtime.Host, targetPlatform with
    | { OS = Runtime.Windows }, { OS = Runtime.Windows } ->
      let env =
        Msvc.vcvars Runtime.Host.Architecture targetPlatform.Architecture

      {|
        Env = EnvMap.ofMap env
        Bin = source </> "build" </> Runtime.Platform.toString targetPlatform
      |}

  let generate (targetPlatform : Runtime.Platform) =
    let toolchain = getToolchain targetPlatform

    CMake.generate (fun p ->

      let args = [
        $"-DCMAKE_MAKE_PROGRAM={Ninja.exe.Value}"
        $"-B{toolchain.Bin}"
      ]

      { p with
          ToolPath = exe.Value
          SourceDirectory = source
          Generator = "Ninja Multi-Config"
          AdditionalArgs = String.concat " " args
      })
    |> CreateProcess.withEnvironmentMap toolchain.Env
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

  let build (targetPlatform : Runtime.Platform) =
    let toolchain = getToolchain targetPlatform

    CMake.build (fun p ->
      { p with
          ToolPath = exe.Value
          BinaryDirectory = toolchain.Bin
      })
    |> CreateProcess.withEnvironmentMap toolchain.Env
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

module Fantomas =
  let private fantomas = DotNet.exec id "fantomas"

  let private sources =
    !!(Path.root </> "**/*.fs")
    |> Seq.filter (fun s -> not <| Path.ignored.IsIgnored (s, false))
    |> Seq.toList

  let check files =
    defaultArg files sources
    |> String.concat " "
    |> sprintf "%s --check"
    |> fantomas
    |> function
      | Exit.Code 0 _ -> Trace.log "No fsharp files need formatting."
      | Exit.Code 99 _ ->
        Trace.log "==> Run `./build Format.FSharp.format` to apply formatting."

        raise <| exn "Some fsharp files need formatting."
      | _ -> raise <| exn "Error while formatting fsharp files."

  let format () =
    sources
    |> String.concat " "
    |> fantomas
    |> function
      | Exit.OK _ -> ()
      | _ -> raise <| exn "Error while formatting fsharp files."

module Target =
  open FsToolkit.ErrorHandling

  module private Cpp =
    let parseArgs (doc : Docopt.Doc) (args : string list) =
      let parameters = validation {
        let! args = doc.TryParse args

        let! platforms =
          args
          |> DocoptResult.tryGetArguments "--platform"
          |> Option.defaultValue []
          |> List.traverseResultA (fun str ->
            match Runtime.Platform.tryParse str with
            | Some p -> Ok p
            | None -> Error $"'{str}' is not a valid platform")
          |> Result.map (function
            | [] -> [ Runtime.Host ]
            | xs -> xs)
          |> Result.map Set

        return {| Platforms = platforms |}
      }

      match parameters with
      | Validation.Ok p -> p
      | Validation.Error e ->
        "Invalid arguments: " + String.concat "; " e |> Target.fail


  // Target names like {Phase}.{Subject}[.{action}]
  let init () =
    Target.initEnvironment ()

    Target.create "Tool.FSharp.check" (fun p ->
      match p.Context.Arguments with
      | [] -> Fantomas.check None
      | files -> Fantomas.check (Some files))

    Target.create "Tool.FSharp.format" (fun _ -> Fantomas.format ())

    Target.create "Restore.Cpp" (fun p ->
      let doc =
        Docopt.create
          """usage: Restore.Cpp.restore [--platform=<platform>]..."""

      let parameters = Cpp.parseArgs doc p.Context.Arguments

      for platform in parameters.Platforms do
        CMake.generate platform)

    Target.create "Build.Cpp" (fun p ->
      let doc =
        Docopt.create """usage: Build.Cpp.build [--platform=<platform>]..."""

      let parameters = Cpp.parseArgs doc p.Context.Arguments

      for platform in parameters.Platforms do
        CMake.build platform)

    Target.create "Restore.DotNet" (fun _ -> Paket.restore ())

    "Restore.Cpp" ==> "Build.Cpp"




[<EntryPoint>]
let main argv =
  argv
  |> Array.toList
  |> Context.FakeExecutionContext.Create false "build.fsx"
  |> Context.RuntimeContext.Fake
  |> Context.setExecutionContext


  // failwith MSBuild.msBuildExe
  let _ = Target.init ()
  let ctx = Target.WithContext.runOrDefaultWithArguments "Build.Cpp"
  Target.updateBuildStatus ctx

  match Target.results ctx with
  | Ok () -> 0
  | Error _ -> 1 // already logged by the reporter

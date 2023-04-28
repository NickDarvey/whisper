open System
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.Tools.Git
open MAB.DotIgnore

/// The definition of this solution.
[<AutoOpen>]
module private Solution =
  type Configuration =
    | Debug
    | Release

  module Configuration =
    /// All supported configurations for this solution.
    let all = Set [ Debug ; Release ]

    let toString =
      function
      | Debug -> "Debug"
      | Release -> "Release"

    let tryParse =
      function
      | "Debug" -> Some Debug
      | "Release" -> Some Release
      | _ -> None

  type OS = | Windows

  module OS =
    let toString (os : OS) =
      match os with
      | Windows -> "windows"

    let tryParse =
      function
      | "windows" -> Some Windows
      | _ -> None

  type Architecture =
    | X64
    | X86

  module Architecture =
    let toString (architecture : Architecture) =
      match architecture with
      | X64 -> "x64"
      | X86 -> "x86"

    let tryParse =
      function
      | "x64" -> Some X64
      | "x86" -> Some X86
      | _ -> None

  /// A description for native development based on [Vcpkg's triplets](https://github.com/microsoft/vcpkg/blob/master/docs/users/triplets.md).
  type Platform = {
    OS : OS
    Architecture : Architecture
  }

  module Platform =
    open FsToolkit.ErrorHandling

    let toString (platform : Platform) =
      $"{Architecture.toString platform.Architecture}-{OS.toString platform.OS}"

    let tryParse str =
      match str |> String.split '-' with
      | [ arch ; os ] -> option {
          let! os = OS.tryParse os
          let! arch = Architecture.tryParse arch
          return { OS = os ; Architecture = arch }
        }
      | _ -> None

    open System.Runtime.InteropServices

    /// The current (host) platform.
    let host = {
      OS =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
          Windows
        else
          invalidOp
            $"{RuntimeInformation.OSDescription} is an unsupported operating system"
      Architecture =
        match RuntimeInformation.ProcessArchitecture with
        | Architecture.X86 -> X86
        | Architecture.X64 -> X64
        | arch -> invalidOp $"{arch} is an unsupported architecture"
    }

    /// All supported (target) platforms for this solution as a type map.
    type Targets<'a> =
      {
        ``x86-windows`` : 'a
        ``x64-windows`` : 'a
      }

      member this.all () = [ this.``x86-windows`` ; this.``x64-windows`` ]

    let Targets = {
      ``x86-windows`` = { OS = Windows ; Architecture = X86 }
      ``x64-windows`` = { OS = Windows ; Architecture = X64 }
    }

    let targets = Set <| Targets.all ()

  module Path =
    let root = __SOURCE_DIRECTORY__ </> ".."
    let ignored = IgnoreList (root </> ".gitignore")
    let dist = root </> "dist"

/// Tools used by this solution (which could probably be moved elsewhere).
[<AutoOpen>]
module private Tools =
  module DotNet =
    let install = lazy DotNet.install DotNet.Versions.FromGlobalJson

  // let private execWithBinLog project common command args msBuildArgs =
  //     let argString = MSBuild.fromCliArguments msBuildArgs

  //     let binLogPath, args =
  //         addBinaryLogger msBuildArgs.DisableInternalBinLog (args + " " + argString) common

  //     let result = exec (fun _ -> common) command args
  //     MSBuild.handleAfterRun (sprintf "dotnet %s" command) binLogPath result.ExitCode project

  module Paket =
    let files = Path.root </> "paket-files"

    let restore () =
      Paket.restore (fun p ->
        { p with
            ToolType = ToolType.CreateLocalTool DotNet.install.Value
        })

  module Ninja =
    let exe =
      lazy
        (let binary =
          match Platform.host.OS with
          | Windows -> "ninja.exe"

         Paket.files </> "**" </> binary
         |> GlobbingPattern.create
         |> Seq.exactlyOne)

  module Msvc =
    open BlackFox.VsWhere

    /// The requested version of MSVC
    [<Literal>]
    let Version = "14.35"

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
        | X64, X64 -> "amd64"
        | X64, X86 -> "amd64_x86"
        | X86, X86 -> "x86"
        | X86, X64 -> "x86_amd64"

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

  module Swig =
    let exe =
      lazy
        (let binary =
          match Platform.host.OS with
          | Windows -> "swigwin-*/swig.exe"

         Paket.files </> "**" </> binary
         |> GlobbingPattern.create
         |> Seq.exactlyOne)

    type LanguageOptions =
      | CSharp of
        {|
          DllImport : string
          Namespace : string
        |}

    type InputOptions = | InputFiles of string list

    type GeneralOptions = {
      /// Language-specific options
      Language : LanguageOptions
      /// Enable C++ processing
      EnableCppProcessing : bool
      /// Path for the output (wrapper) file
      OutputFile : string
      /// Path for language-specific files
      OutputDirectory : string
      /// Path(s) for input file(s)
      Input : InputOptions
    }

    let private args opts =
      let args =
        Arguments.Empty
        |> Arguments.appendIf opts.EnableCppProcessing "-c++"
        |> Arguments.append [ "-o" ; opts.OutputFile ]
        |> Arguments.append [ "-outdir" ; opts.OutputDirectory ]

      let args =
        match opts.Language with
        | CSharp opts ->
          args
          |> Arguments.append [ "-csharp" ]
          |> Arguments.append [ "-dllimport" ; opts.DllImport ]
          |> Arguments.append [ "-namespace" ; opts.Namespace ]

      let args =
        match opts.Input with
        | InputFiles files -> args |> Arguments.append files

      Arguments.toList args

    let run opts =
      args opts |> CreateProcess.fromRawCommand exe.Value

  module CMake =

    let exe =
      lazy
        (let binary =
          match Platform.host.OS, Platform.host.Architecture with
          | Windows, X86 -> "windows-i386/bin/cmake.exe"
          | Windows, X64 -> "windows-x86_64/bin/cmake.exe"

         Paket.files </> "**" </> $"cmake-*-{binary}"
         |> GlobbingPattern.create
         |> Seq.exactlyOne)

  module Vcpkg =
    let private folder =
      lazy
        (Paket.files </> "**" </> "microsoft" </> "vcpkg"
         |> GlobbingPattern.create
         |> Seq.exactlyOne)

    let exe = lazy (folder.Value </> "vcpkg.exe")

    let toolchain =
      lazy (folder.Value </> "scripts" </> "buildsystems" </> "vcpkg.cmake")

    // https://github.com/microsoft/vcpkg/blob/master/docs/users/triplets.md
    let toTriplet platform =
      match platform with
      | { OS = Windows ; Architecture = X64 } -> "x64-windows"
      | { OS = Windows ; Architecture = X86 } -> "x86-windows"

  /// Can be used to set the PATH environment variable so that the above tools are available to other tools.
  let PATH =
    lazy
      (Environment.GetEnvironmentVariable "PATH"
       |> String.split IO.Path.PathSeparator
       |> List.append [ Swig.exe.Value ; CMake.exe.Value ; Vcpkg.exe.Value ]
       |> List.map Path.getDirectory
       |> String.separated $"%c{IO.Path.PathSeparator}")

/// The build actions for this solution.
module private Actions =

  module fsharp =
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
          Trace.log "==> Run `build fsharp:format` to apply formatting."

          Target.fail "Some fsharp files need formatting."
        | _ -> raise <| exn "Error while formatting fsharp files."

    let format () =
      sources
      |> String.concat " "
      |> fantomas
      |> function
        | Exit.OK _ -> ()
        | _ -> raise <| exn "Error while formatting fsharp files."


  module cpp =
    open Fake.Build
    open System.Text.RegularExpressions

    let source = Path.root
    let out = source </> "out"

    module dotnet =
      /// The name for the generated wrapper file.
      let wrapperFileName = "Whisper.g.cxx"
      /// The name for the library
      let libraryName = "whisper"
      /// The namespace for the runtime package
      let runtimeNamespace = "Whisper.Runtime"

    let private getToolchain (targetPlatform : Platform) =
      match Platform.host, targetPlatform with
      | { OS = Windows }, { OS = Windows } ->
        let env =
          Msvc.vcvars Platform.host.Architecture targetPlatform.Architecture

        {|
          Env = EnvMap.ofMap env
          Bin = out </> Platform.toString targetPlatform
          Architecture =
            match targetPlatform.Architecture with
            | X86 -> "win32"
            | X64 -> "x64"
        |}

    let getVersion () =
      System.IO.File.ReadLines (source </> "CMakeLists.txt")
      |> Seq.choose (fun l ->
        let result = Regex.Match(l, "add_subdirectory\(([\w\/\.]+)\)")
        if result.Success then Some result.Groups[1].Value else None)
      |> Seq.collect (fun dir -> System.IO.File.ReadLines (dir </> "CMakeLists.txt"))
      |> Seq.choose (fun l ->
        let result = Regex.Match(l, "project\(.+?VERSION (\d+)\.(\d+)\.(\d+)")
        if result.Success then Some (result.Groups[1].Value, result.Groups[2].Value, result.Groups[3].Value) else None)
      |> Seq.tryExactlyOne
      |> function
      | Some (major, minor, revision) ->
        System.Int32.Parse major, System.Int32.Parse minor, System.Int32.Parse revision
      | _ -> invalidOp $"Couldn't find a valid version number for whisper.cpp"

    let configure (targetPlatform : Platform) =
      let toolchain = getToolchain targetPlatform

      CMake.generate (fun p ->

        let args = [
          $"-B{toolchain.Bin}"
          $"-DCMAKE_CONFIGURATION_TYPES={String.Join (';', Configuration.all |> Seq.map Configuration.toString)}"
          $"-DCMAKE_TOOLCHAIN_FILE={Vcpkg.toolchain.Value}"
          $"-DVCPKG_TARGET_TRIPLET={Vcpkg.toTriplet targetPlatform}"
          $"-DWHISPER_SUPPORT_OPENBLAS=OFF"
          $"-DDOTNET_WRAPPER_FILE_NAME={dotnet.wrapperFileName}"
          $"-DDOTNET_LIBRARY_NAME={dotnet.libraryName}"
        ]

        { p with
            ToolPath = CMake.exe.Value
            SourceDirectory = source
            // I tried Ninja but it resulted in woeful performance of Whisper.
            // I'm sure it's not Ninja's fault, but the defaults with the Visual Studio generator work well and I can't be bothered investigating.
            // Generator = "Ninja Multi-Config"
            Generator = "Visual Studio 17 2022" //
            Platform = toolchain.Architecture
            AdditionalArgs = String.concat " " args
        })
      |> CreateProcess.withEnvironmentMap toolchain.Env
      |> CreateProcess.ensureExitCode
      |> Proc.run
      |> ignore

    let build (targetPlatform : Platform) (configuration : Configuration) =
      let toolchain = getToolchain targetPlatform

      CMake.build (fun p ->
        { p with
            ToolPath = CMake.exe.Value
            BinaryDirectory = toolchain.Bin
            Config = Configuration.toString configuration
        })
      |> CreateProcess.withEnvironmentMap toolchain.Env
      |> CreateProcess.ensureExitCode
      |> Proc.run
      |> ignore

    let clean () = Directory.delete out

    let vcpkg args =
      CreateProcess.fromRawCommand Vcpkg.exe.Value args
      // CreateProcess.fromRawCommandLine "cmd" "/C \"echo %PATH%\""
      |> CreateProcess.setEnvironmentVariable "PATH" PATH.Value
      |> CreateProcess.ensureExitCode
      |> Proc.run
      |> ignore

  module dotnet =
    let relative = "src" </> "dotnet"

    let sln = relative </> "dotnet.sln"
    let runtime = relative </> "runtime"

    let private common opts =
      let opts = DotNet.install.Value opts

      { opts with
          WorkingDirectory = Path.root
      }

    let private cfg configuration =
      match configuration with
      | Debug -> DotNet.BuildConfiguration.Debug
      | Release -> DotNet.BuildConfiguration.Release

    let private props configuration = []

    let private dist configuration =
      Path.dist </> Configuration.toString configuration </> "dotnet"

    let private getVersion () =
      let incr = System.Int32.Parse <| File.readLine (relative </> "version")
      let major, minor, revision = cpp.getVersion ()
      // we put our own increments into revision space, so shift the whisper.cpp revision number to make room.
      // (this means we can release 999 versions for every 1 whisper.cpp version.)
      let revision = revision * 1_000 + incr
      $"{major}.{minor}.{revision}"

    let clean () =
      File.deleteAll (
        GlobbingPattern.create (Path.root </> relative </> "**/*.g.*")
      )

      for config in Configuration.all do
        let res =
          DotNet.exec
            (fun x ->
              { x.WithCommon (common) with
                  CustomParams = Some $"--configuration {cfg config}"
              })
            "clean"
            sln

        if not res.OK then
          failwith <| failwith $"{String.toLines res.Errors}"

        Directory.delete (dist config)


    let restore () =
      DotNet.restore (fun x -> {
        x.WithCommon common with 
          MSBuildParams = {
            x.MSBuildParams with 
              Properties = x.MSBuildParams.Properties @ [("Configuration", "Release"); ("Platform","Any CPU")]
          }
      }) sln

    let generate () =

      File.deleteAll (
        GlobbingPattern.create (Path.root </> relative </> "**/*.g.*")
      )

      let files = IO.Directory.CreateTempSubdirectory ()

      Swig.run
        {
          Language =
            Swig.CSharp
              {|
                DllImport = cpp.dotnet.libraryName
                Namespace = cpp.dotnet.runtimeNamespace
              |}
          EnableCppProcessing = true
          OutputFile = runtime </> cpp.dotnet.wrapperFileName
          OutputDirectory = files.FullName
          Input = Swig.InputFiles [ runtime </> "whisper.i" ]
        }
      |> CreateProcess.ensureExitCode
      |> Proc.run
      |> ignore

      for file in files.EnumerateFiles () do
        let target =
          file.FullName
          |> Path.toRelativeFrom files.FullName
          |> Path.changeExtension $".g{file.Extension}"
          |> Path.combine runtime

        file.MoveTo target

      files.Delete true

    let build configuration =
      DotNet.build
        (fun x ->
          { x with
              Common = common x.Common
              NoRestore = true
              Configuration = cfg configuration 
              MSBuildParams =
                { x.MSBuildParams with
                    Properties = props configuration
                    DisableInternalBinLog = true
                    BinaryLoggers = Some ["dotnetbuild.binlog"]
                }
          })
        sln

    let test configuration testResultsDirectory =
      DotNet.test
        (fun x ->
          { x with
              Common = common x.Common
              NoBuild = true
              NoRestore = true
              Logger = testResultsDirectory |> Option.map (fun _ -> "trx")
              ResultsDirectory = testResultsDirectory
              Configuration = cfg configuration
              MSBuildParams =
                { x.MSBuildParams with
                    Properties = props configuration
                }
          })
        sln

    let pack configuration =
      DotNet.pack
        (fun x ->
          { x with
              Common = common x.Common
              Configuration = cfg configuration
              NoRestore = true
              NoBuild = true
              OutputPath = Some (dist configuration)
              MSBuildParams = {
                x.MSBuildParams with
                  Properties = [ "Version", getVersion () ]
              }
          })
        sln


module private Target =
  open Argu

  [<RequiresExplicitTypeArguments>]
  let createWithArgs<'args when 'args :> IArgParserTemplate> name f =
    let parser = ArgumentParser.Create<'args> (programName = name)

    Target.create name (fun p ->
      let results =
        parser.Parse (
          Array.ofList p.Context.Arguments,
          ignoreUnrecognized = true // TODO track and handle unused args across multiple targets
        )

      f results)

/// Actions integrated with CLI args.
module private Args =
  open Argu
  open FsToolkit.ErrorHandling

  let parsePlatforms postProcess =
    let platforms =
      postProcess
      <| fun arg ->
           match Platform.tryParse arg with
           | Some p when Platform.targets.Contains p -> p
           | Some p -> failwith $"'{p}' is not a supported platform"
           | None -> failwith $"'{arg}' is not a valid platform"

    let platforms =
      if List.isEmpty platforms then
        [ Platform.host ]
      else
        platforms

    Set platforms

  let parseConfigurations postProcess =
    let configurations =
      postProcess
      <| fun arg ->
           match Configuration.tryParse arg with
           | Some c when Configuration.all.Contains c -> c
           | Some c -> failwith $"'{c}' is not a supported configuration"
           | None -> failwith $"'{arg}' is not a valid configuration"

    let configurations =
      if List.isEmpty configurations then
        [ Debug ]
      else
        configurations

    Set configurations

  module fsharp =

    [<RequireQualifiedAccess>]
    type check =
      | [<MainCommand ; Last>] Files of file : string list

      interface IArgParserTemplate with
        member s.Usage =
          match s with
          | Files _ -> "specify files to check"

  module cpp =

    [<RequireQualifiedAccess>]
    type configure =
      | Platform of platform : string

      interface IArgParserTemplate with
        member s.Usage =
          match s with
          | Platform _ -> "specify a target platform"

    [<RequireQualifiedAccess>]
    type build =
      | Configuration of configuration : string
      | Platform of platform : string

      interface IArgParserTemplate with
        member s.Usage =
          match s with
          | Configuration _ -> "specify zero or more configurations"
          | Platform _ -> "specify zero or more target platforms"

  module dotnet =
    [<RequireQualifiedAccess>]
    type build =
      | Configuration of configuration : string

      interface IArgParserTemplate with
        member s.Usage =
          match s with
          | Configuration _ -> "specify zero or more configurations"

    [<RequireQualifiedAccess>]
    type test =
      | Configuration of configuration : string
      | Results of path : string

      interface IArgParserTemplate with
        member s.Usage =
          match s with
          | Configuration _ -> "specify zero or more configurations"
          | Results _ -> "specify a directory for test results"



// Target names like {subject}:{action}
let init () =
  Target.initEnvironment ()

  Target.createWithArgs<Args.fsharp.check> "fsharp:check" (fun args ->
    let files = args.TryGetResult Args.fsharp.check.Files
    Actions.fsharp.check files)

  Target.create "fsharp:format" (fun _ -> Actions.fsharp.format ())

  Target.create "cpp:clean" (fun _ -> Actions.cpp.clean ())

  Target.createWithArgs<Args.cpp.configure> "cpp:configure" (fun args ->
    let platforms =
      Args.parsePlatforms (fun p ->
        args.PostProcessResults (Args.cpp.configure.Platform, p))

    for platform in platforms do
      Actions.cpp.configure platform)

  Target.createWithArgs<Args.cpp.build> "cpp:build" (fun args ->
    let platforms =
      Args.parsePlatforms (fun p ->
        args.PostProcessResults (Args.cpp.build.Platform, p))

    let configurations =
      Args.parseConfigurations (fun p ->
        args.PostProcessResults (Args.cpp.build.Configuration, p))

    for (platform, configuration) in Seq.allPairs platforms configurations do
      Actions.cpp.build platform configuration)

  Target.create "cpp:vcpkg" (fun p -> Actions.cpp.vcpkg p.Context.Arguments)

  Target.create "dotnet:clean" (fun p -> Actions.dotnet.clean ())

  Target.create "dotnet:restore" (fun p -> Actions.dotnet.restore ())

  Target.create "dotnet:generate" (fun p -> Actions.dotnet.generate ())


  Target.createWithArgs<Args.dotnet.build> "dotnet:build" (fun args ->
    let configurations =
      Args.parseConfigurations (fun p ->
        args.PostProcessResults (Args.dotnet.build.Configuration, p))

    for configuration in configurations do
      Actions.dotnet.build configuration)

  Target.createWithArgs<Args.dotnet.test> "dotnet:test" (fun args ->
    let configurations =
      Args.parseConfigurations (fun p ->
        args.PostProcessResults (Args.dotnet.test.Configuration, p))

    for configuration in configurations do
      Actions.dotnet.test configuration (args.TryGetResult Args.dotnet.test.Results))

  Target.createWithArgs<Args.dotnet.build> "dotnet:pack" (fun args ->
    let configurations =
      Args.parseConfigurations (fun p ->
        args.PostProcessResults (Args.dotnet.build.Configuration, p))

    for configuration in configurations do
      Actions.dotnet.pack configuration)

  Target.create "clean" ignore
  Target.create "restore" ignore
  Target.create "build" ignore
  Target.create "test" ignore
  Target.create "pack" ignore

  "clean" <== [ "cpp:clean" ; "dotnet:clean" ]
  "restore" <== [ "dotnet:restore" ; "cpp:configure" ; "dotnet:generate" ]
  "build" <== [ "dotnet:build" ; "cpp:build" ]
  "test" <== [ "dotnet:test" ]
  "pack" <== [ "dotnet:pack" ]

  let _ = "clean" ?=> "restore" ==> "build"
  let _ = "build" ==> "test"
  let _ = "build" ==> "pack"
  ()




[<EntryPoint>]
let main argv =
  argv
  |> Array.toList
  |> Context.FakeExecutionContext.Create false "build.fsx"
  |> Context.RuntimeContext.Fake
  |> Context.setExecutionContext

  let _ = init ()
  let ctx = Target.WithContext.runOrDefaultWithArguments "build"
  Target.updateBuildStatus ctx

  match Target.results ctx with
  | Ok () -> 0
  | Error _ -> 1 // already logged by the reporter

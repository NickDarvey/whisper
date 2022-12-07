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

module private Swig =
  let exe =
    lazy
      (let binary =
        match Runtime.Host.OS with
        | Runtime.Windows -> "swigwin-*/swig.exe"

       Paket.files </> "**" </> binary
       |> GlobbingPattern.create
       |> Seq.exactlyOne)

  type LanguageOptions = | CSharp of {| DllImport : string |}

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

    let args =
      match opts.Input with
      | InputFiles files -> args |> Arguments.append files

    Arguments.toList args

  let run opts =
    args opts |> CreateProcess.fromRawCommand exe.Value

module private CMake =

  let exe =
    lazy
      (let binary =
        match Runtime.Host.OS, Runtime.Host.Architecture with
        | Runtime.Windows, Runtime.X86 -> "windows-i386/bin/cmake.exe"
        | Runtime.Windows, Runtime.X64 -> "windows-x86_64/bin/cmake.exe"

       Paket.files </> "**" </> $"cmake-*-{binary}"
       |> GlobbingPattern.create
       |> Seq.exactlyOne)

module private Projects =
  module src =
    module dotnet =
      let path = Path.root </> "src" </> "dotnet"
      /// The name for the generated wrapper file.
      let wrapperFileName = "Whisper.g.cxx"
      /// The name for the library
      let libraryFileName = "whisper"

      let generate () =

        File.deleteAll (!!(path </> "**/*.g.*"))

        let files = IO.Directory.CreateTempSubdirectory ()

        Swig.run
          {
            Language = Swig.CSharp {| DllImport = "test" |}
            EnableCppProcessing = true
            OutputFile = path </> wrapperFileName
            OutputDirectory = files.FullName
            Input = Swig.InputFiles [ path </> "whisper.i" ]
          }
        |> CreateProcess.ensureExitCode
        |> Proc.run
        |> ignore

        for file in files.EnumerateFiles () do
          let target =
            file.FullName
            |> Path.toRelativeFrom files.FullName
            |> Path.changeExtension $".g{file.Extension}"
            |> Path.combine path

          file.MoveTo target

        files.Delete true

      let clean () =
        File.deleteAll (GlobbingPattern.create (path </> "**/*.g.*"))


module private Subjects =
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

    let source = Path.root
    let out = source </> "out"

    let private getToolchain (targetPlatform : Runtime.Platform) =
      match Runtime.Host, targetPlatform with
      | { OS = Runtime.Windows }, { OS = Runtime.Windows } ->
        let env =
          Msvc.vcvars Runtime.Host.Architecture targetPlatform.Architecture

        {|
          Env = EnvMap.ofMap env
          Bin = out </> Runtime.Platform.toString targetPlatform
        |}

    let configure (targetPlatform : Runtime.Platform) =
      let toolchain = getToolchain targetPlatform

      CMake.generate (fun p ->

        let args = [
          $"-B{toolchain.Bin}"
          $"-DCMAKE_MAKE_PROGRAM={Ninja.exe.Value}"
          $"-DSWIG_EXECUTABLE={Swig.exe.Value}"
          $"-DSRC_DOTNET_WRAPPER_FILE_NAME={Projects.src.dotnet.wrapperFileName}"
          $"-DSRC_DOTNET_LIBRARY_FILE_NAME={Projects.src.dotnet.libraryFileName}"
        ]

        { p with
            ToolPath = CMake.exe.Value
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
            ToolPath = CMake.exe.Value
            BinaryDirectory = toolchain.Bin
        })
      |> CreateProcess.withEnvironmentMap toolchain.Env
      |> CreateProcess.ensureExitCode
      |> Proc.run
      |> ignore

    let clean () = Directory.delete out



module Target =
  open FsToolkit.ErrorHandling

  module private cpp =
    let parseArgs (p : TargetParameter) =
      let doc =
        Docopt.create
          $"""usage: %s{p.TargetInfo.Name} [--platform=<platform>]..."""

      let parameters = validation {
        let! args = doc.TryParse p.Context.Arguments

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


  // Target names like {subject}:{action}
  let init () =
    Target.initEnvironment ()

    Target.create "fsharp:check" (fun p ->
      match p.Context.Arguments with
      | [] -> Subjects.fsharp.check None
      | files -> Subjects.fsharp.check (Some files))

    Target.create "fsharp:format" (fun _ -> Subjects.fsharp.format ())

    Target.create "cpp:clean" (fun p -> Subjects.cpp.clean ())

    Target.create "cpp:configure" (fun p ->
      let parameters = cpp.parseArgs p

      for platform in parameters.Platforms do
        Subjects.cpp.configure platform)

    Target.create "cpp:build" (fun p ->
      let parameters = cpp.parseArgs p

      for platform in parameters.Platforms do
        Subjects.cpp.build platform)

    Target.create "src/dotnet:clean" (fun p -> Projects.src.dotnet.clean ())

    Target.create "src/dotnet:generate" (fun p ->
      Projects.src.dotnet.generate ())

    Target.create "clean" ignore
    Target.create "restore" ignore
    Target.create "build" ignore

    "clean" <== [ "src/dotnet:clean" ; "cpp:clean" ]
    "restore" <== [ "cpp:configure" ; "src/dotnet:generate" ]
    "build" <== [ "cpp:build" ]

    "clean" ?=> "restore" ==> "build"



[<EntryPoint>]
let main argv =
  argv
  |> Array.toList
  |> Context.FakeExecutionContext.Create false "build.fsx"
  |> Context.RuntimeContext.Fake
  |> Context.setExecutionContext

  let _ = Target.init ()
  let ctx = Target.WithContext.runOrDefaultWithArguments "Build.Cpp"
  Target.updateBuildStatus ctx

  match Target.results ctx with
  | Ok () -> 0
  | Error _ -> 1 // already logged by the reporter

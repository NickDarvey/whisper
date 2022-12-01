open System
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open MAB.DotIgnore

module Exit =
  let (|Code|_|) code (result : ProcessResult) =
    if result.ExitCode = code then Some result else None

  let (|OK|_|) (result : ProcessResult) =
    if result.OK then Some result else None

module Path =
  let root = __SOURCE_DIRECTORY__ </> ".."
  let ignored = IgnoreList (root </> ".gitignore")

module Format =
  module FSharp =
    let private fantomas = DotNet.exec id "fantomas"

    let private sources =
      !!(Path.root </> "**/*.fs")
      |> Seq.filter (fun s -> not <| Path.ignored.IsIgnored (s, false))

    let check _ =
      sources
      |> String.concat " "
      |> sprintf "%s --check"
      |> fantomas
      |> function
        | Exit.Code 0 _ -> Trace.log "No fsharp files need formatting."
        | Exit.Code 99 _ ->
          Trace.traceError "Some fsharp files need formatting."
        | _ -> Trace.traceError "Error while formatting fsharp files."

    let format _ =
      sources
      |> String.concat " "
      |> fantomas
      |> function
        | Exit.OK _ -> ()
        | _ -> Trace.traceError "Error while formatting fsharp files."

module Target =

  let init () =
    Target.initEnvironment ()
    Target.create "Format.FSharp.check" Format.FSharp.check
    Target.create "Format.FSharp.format" Format.FSharp.format

[<EntryPoint>]
let main argv =
  argv
  |> Array.toList
  |> Context.FakeExecutionContext.Create false "build.fsx"
  |> Context.RuntimeContext.Fake
  |> Context.setExecutionContext

  Target.init ()
  Target.runOrDefaultWithArguments "Format.FSharp.check"

  0 // return an integer exit code

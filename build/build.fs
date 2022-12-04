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

module Format =
  module FSharp =
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
          Trace.log
            "==> Run `./build Format.FSharp.format` to apply formatting."

          raise <| exn "Some fsharp files need formatting."
        | _ -> raise <| exn "Error while formatting fsharp files."

    let format _ =
      sources
      |> String.concat " "
      |> fantomas
      |> function
        | Exit.OK _ -> ()
        | _ -> raise <| exn "Error while formatting fsharp files."

module Restore =
  module DotNet =
    let restore _ =
      Paket.restore (fun p ->
        { p with
            ToolType = ToolType.CreateLocalTool DotNet.install.Value
        })

module Target =

  let init () =
    Target.initEnvironment ()

    Target.create "Format.FSharp.check" (fun p ->
      match p.Context.Arguments with
      | [] -> Format.FSharp.check None
      | files -> Format.FSharp.check (Some files))

    Target.create "Format.FSharp.format" Format.FSharp.format

    Target.create "Restore.DotNet.restore" Restore.DotNet.restore

[<EntryPoint>]
let main argv =
  argv
  |> Array.toList
  |> Context.FakeExecutionContext.Create false "build.fsx"
  |> Context.RuntimeContext.Fake
  |> Context.setExecutionContext

  Target.init ()
  let ctx = Target.WithContext.runOrDefaultWithArguments "Format.FSharp.check"
  Target.updateBuildStatus ctx
  if Target.isFailed ctx then 1 else 0

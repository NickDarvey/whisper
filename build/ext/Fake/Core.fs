namespace Fake.Core

open Fake.Core

module Exit =
  let (|Code|_|) code (result : ProcessResult) =
    if result.ExitCode = code then Some result else None

  let (|OK|_|) (result : ProcessResult) =
    if result.OK then Some result else None

module Docopt =

  type Doc = {
    Usage : string
    TryParse : seq<string> -> Result<Map<string, DocoptResult>, string>
  }

  let create doc =

    let d = Docopt doc

    let tryParse (args : #seq<string>) =
      try
        Ok <| d.Parse args
      with DocoptException msg ->
        Error msg

    { Usage = doc ; TryParse = tryParse }

module Target =
  exception TargetFailed of message : string

  /// <summary>
  /// If `TargetContext option` is Some and has error, raise it as a BuildFailedException
  /// </summary>
  ///
  /// <param name="context">The target context</param>
  let isFailed (context : Target.OptionalTargetContext) =
    let c = context.Context

    c.IsSome
    && c.Value.HasError
    && not c.Value.CancellationToken.IsCancellationRequested

  let inline fail msg = raise <| TargetFailed msg

  /// Handles the results from the target contexts.
  /// - If there's a failure, returns it as an error,
  /// - else if there's an exception, raises it as an exception,
  /// - else returns.
  let results (context : Target.OptionalTargetContext) =
    match context.Context with
    | None -> Ok ()
    | Some ctx when ctx.CancellationToken.IsCancellationRequested -> Ok ()
    | Some ctx when not ctx.HasError -> Ok ()
    | Some ctx ->
      let failures, exceptions =
        ctx.ErrorTargets
        |> List.partitionMap (function
          | (:? TargetFailed as failure, tgt) ->
            Choice1Of2 (tgt.Name, failure.message)
          | ex, tgt -> Choice2Of2 (tgt.Name, ex))

      if exceptions.IsEmpty then
        Error failures
      else
        let msg =
          exceptions
          |> List.map fst
          |> sprintf "Targets %A raised an exception."

        let exn = exceptions |> List.map snd |> System.AggregateException
        raise <| BuildFailedException (ctx, msg, exn)

/// Types for describing native development based on [Vcpkg's triplets](https://github.com/microsoft/vcpkg/blob/master/docs/users/triplets.md).
module Runtime =

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

  let Host = {
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

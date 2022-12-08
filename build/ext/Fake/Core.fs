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

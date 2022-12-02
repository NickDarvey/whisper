namespace Fake.Core

open Fake.Core

module Exit =
  let (|Code|_|) code (result : ProcessResult) =
    if result.ExitCode = code then Some result else None

  let (|OK|_|) (result : ProcessResult) =
    if result.OK then Some result else None

module Docopt =

  let create doc =

    let doc = Docopt doc

    let tryParse p =
      try
        Ok <| doc.Parse p.Context.Arguments
      with DocoptException msg ->
        Error msg

    tryParse

module Target =
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

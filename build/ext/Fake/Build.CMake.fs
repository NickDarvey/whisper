module Fake.Build.CMake

open Fake.Build
open Fake.Core
open System

/// <summary>
/// Converts a file path to a valid CMake format.
/// </summary>
///
/// <param name="path">The path to reformat.</param>
let private FormatCMakePath (path : string) = path.Replace ("\\", "/")

/// <summary>
/// Invokes the CMake executable with the specified arguments.
/// </summary>
///
/// <param name="toolPath">The location of the executable. Automatically found if null or empty.</param>
/// <param name="binaryDir">The location of the binary directory.</param>
/// <param name="args">The arguments given to the executable.</param>
/// <param name="timeout">The CMake execution timeout</param>
let private CallCMake toolPath binaryDir args timeout =
  // CMake expects an existing binary directory.
  // Not defaulted because it would prevent building multiple CMake projects in the same FAKE script.
  if String.IsNullOrEmpty binaryDir then
    failwith "The CMake binary directory is not set."
  // Try to find the CMake executable if not specified by the user.
  let cmakeExe =
    if String.isNotNullOrEmpty toolPath then
      toolPath
    else
      let found = CMake.FindExe "cmake"

      if found <> None then
        found.Value
      else
        failwith "Cannot find the CMake executable."
  // CMake expects the binary directory to be passed as an argument.
  let arguments =
    if (String.IsNullOrEmpty args) then
      "\"" + binaryDir + "\""
    else
      args

  let fullCommand = cmakeExe + " " + arguments
  use __ = Trace.traceTask "CMake" fullCommand

  let result =
    CreateProcess.fromRawCommandLine cmakeExe arguments
    |> CreateProcess.withWorkingDirectory binaryDir
    |> CreateProcess.withTimeout (timeout)

  result

let internal getGenerateArguments (parameters : CMake.CMakeGenerateParams) =
  // CMake expects an existing source directory.
  // Not defaulted because it would prevent building multiple CMake projects in the same FAKE script.
  if String.IsNullOrEmpty parameters.SourceDirectory then
    failwith "The CMake source directory is not set."

  let argsIfNotEmpty format values =
    List.filter String.isNotNullOrEmpty values |> List.map (sprintf format)

  let generator = argsIfNotEmpty "-G \"%s\"" [ parameters.Generator ]

  let toolchain =
    argsIfNotEmpty "-D CMAKE_TOOLCHAIN_FILE:FILEPATH=\"%s\"" [
      FormatCMakePath parameters.Toolchain
    ]

  let toolset = argsIfNotEmpty "-T \"%s\"" [ parameters.Toolset ]
  let platform = argsIfNotEmpty "-A \"%s\"" [ parameters.Platform ]

  let caches =
    parameters.Caches |> List.map FormatCMakePath |> argsIfNotEmpty "-C \"%s\""

  let installDir =
    argsIfNotEmpty "-D CMAKE_INSTALL_PREFIX:PATH=\"%s\"" [
      FormatCMakePath parameters.InstallDirectory
    ]

  let variables =
    parameters.Variables
    |> List.map (fun option ->
      "-D "
      + option.Name
      + match option.Value with
        | CMake.CMakeBoolean (value) -> ":BOOL=" + if value then "ON" else "OFF"
        | CMake.CMakeString (value) -> ":STRING=\"" + value + "\""
        | CMake.CMakeDirPath (value) ->
          FormatCMakePath value |> sprintf ":PATH=\"%s\""
        | CMake.CMakeFilePath (value) ->
          FormatCMakePath value |> sprintf ":FILEPATH=\"%s\"")

  let cacheEntriesToRemove =
    argsIfNotEmpty "-U \"%s\"" parameters.CacheEntriesToRemove

  let args =
    [
      generator
      toolchain
      toolset
      platform
      caches
      installDir
      variables
      cacheEntriesToRemove
      [ parameters.AdditionalArgs ; "\"" + parameters.SourceDirectory + "\"" ]
    ]
    |> List.concat
    |> String.concat " "

  args

/// <summary>
/// Calls <c>cmake</c> to generate a project.
/// </summary>
///
/// <param name="setParams">Function used to manipulate the default CMake parameters. See <c>CMakeGenerateParams</c>.</param>
let generate setParams =
  let parameters = setParams CMake.CMakeGenerateDefaults
  let args = getGenerateArguments parameters

  CallCMake
    parameters.ToolPath
    parameters.BinaryDirectory
    args
    parameters.Timeout

/// <summary>
/// Calls <c>cmake --build</c> to build a project.
/// </summary>
///
/// <param name="setParams">Function used to manipulate the default CMake parameters. See <c>CMakeBuildParams</c>.</param>
let build (setParams : CMake.CMakeBuildParams -> CMake.CMakeBuildParams) =
  let parameters = setParams CMake.CMakeBuildDefaults

  let targetArgs =
    if String.IsNullOrEmpty parameters.Target then
      ""
    else
      " --target \"" + parameters.Target + "\""

  let configArgs =
    if String.IsNullOrEmpty parameters.Config then
      ""
    else
      " --config \"" + parameters.Config + "\""

  let args =
    "--build \""
    + parameters.BinaryDirectory
    + "\""
    + targetArgs
    + configArgs
    + parameters.AdditionalArgs

  CallCMake
    parameters.ToolPath
    parameters.BinaryDirectory
    args
    parameters.Timeout

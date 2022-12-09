# Whisper

## Getting started

### Getting started on Windows

**Prerequisites**

1. Microsoft Visual C++ compiler (MSVC) >= 14.34, included with
   - [Build Tools for Visual Studio 2022](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022)
   - [Visual Studio 2022 with a C++ workload](https://learn.microsoft.com/en-us/cpp/build/vscpp-step-0-installation?view=msvc-170)

## TODO
- [ ] You have to run `build` twice because the first time it generates a whisper.dll that depends on the other whisper.dll.

   **Repro**
   1. Run `build clean`
   1. Run `build build`
   1. Check `./out/x64-windows/src/dotnet/runtime/Debug/whisper.dll` with `dumpbin`.
   1. Run `build build`
   1. Check `./out/x64-windows/src/dotnet/runtime/Debug/whisper.dll` with `dumpbin`.

   **Expected**

   The builds are identical.

   **Actual**

   Build 1:
   ```
   > dumpbin /dependents ./out/x64-windows/src/dotnet/runtime/Debug/whisper.dll
   Dump of file ...

   File Type: DLL

   Image has the following dependencies:

      whisper.dll
      VCRUNTIME140D.dll
      ucrtbased.dll
      KERNEL32.dll
   ```

   Build 2:
   ```
   > dumpbin /dependents ./out/x64-windows/src/dotnet/runtime/Debug/whisper.dll
   Dump of file ...

   File Type: DLL

   Image has the following dependencies:

      KERNEL32.dll
      MSVCP140D.dll
      VCRUNTIME140D.dll
      VCRUNTIME140_1D.dll
      ucrtbased.dll
   ```

   **Ideas**

   - Maybe compare the CMakeCache.txt to see if it's changing between build 1 and build 2.
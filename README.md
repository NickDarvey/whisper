# Whisper

## Getting started

### Getting started on Windows

**Prerequisites**

1. Microsoft Visual C++ compiler (MSVC) >= 14.34, included with
   - [Build Tools for Visual Studio 2022](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022)
   - [Visual Studio 2022 with a C++ workload](https://learn.microsoft.com/en-us/cpp/build/vscpp-step-0-installation?view=msvc-170)

## TODO
- [ ] `runtime.csproj` native dependencies aren't copied into the right place when using `build build`, but are after running `dotnet build src/dotnet`.
   It seems to ignore the `LinkBase` when running `build build.` I think this may be because of Fake.Msbuild's poor escaping of the command line argument `LibraryFileNameHost`.

   A solution may be to code around the escaping it is doing.

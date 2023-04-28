# Whisper

[![NuGet Badge](https://buildstats.info/nuget/Whisper.Runtime)](https://www.nuget.org/packages/Whisper.Runtime)

High-performance inference of OpenAI's Whisper automatic speech recognition (ASR) model.
     
This is a .NET wrapper around the native implementation of Whisper, [whisper.cpp](https://github.com/ggerganov/whisper.cpp), by Georgi Gerganov.

## Getting started

1. Install `Whisper.Runtime` from NuGet.org
1. Use [something like this](https://github.com/NickDarvey/whisper/blob/trunk/src/dotnet/examples/example.cs#L110-L133).
   (See also [#9](https://github.com/NickDarvey/whisper/issues/9).)

## Supported platforms

This package contains native libraries so your platform needs to be explicitly supported.

1. win-x64
1. win-x86
1. ... [#6](https://github.com/NickDarvey/whisper/issues/6)

## Contributing

### Contributing on Windows

**Prerequisites**

1. Microsoft Visual C++ compiler (MSVC) 14.35, included with
   - [Build Tools for Visual Studio 2022](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022)
   - [Visual Studio 2022 with a C++ workload](https://learn.microsoft.com/en-us/cpp/build/vscpp-step-0-installation?view=msvc-170)

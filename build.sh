#!/usr/bin/env bash

set -eu
set -o pipefail

dotnet tool restore 1> /dev/null
dotnet run --project ./build/build.fsproj -- -t "$@"
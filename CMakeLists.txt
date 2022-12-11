cmake_minimum_required(VERSION 3.20)

# Supress warning about unused args
# https://stackoverflow.com/a/51444436/1259408
MESSAGE(STATUS "Using toolchain file: ${CMAKE_TOOLCHAIN_FILE}")

project(Whisper VERSION 1.0 LANGUAGES CXX)

# Bundle the whisper library inside the wrapper library
set(BUILD_SHARED_LIBS OFF)

add_subdirectory(ext/whisper.cpp)

# Workaround for making OpenBLAS headers available to Whisper.
#   Error:
#    > MSBuild version 17.4.0+18d5aef85 for .NET Framework
#    > ggml.c
#    > .\whisper\ext\whisper.cpp\ggml.c(98,1): fatal  error C1083: Cannot open include file: 'cblas.h': No such file or directory [.\out\x64-windows\ext\whisper.cpp\whisper.vcxproj]
#   Investigation:
#    1. I'm not sure if vcpkg is supposed to include headers do this automatically, but it doesn't.
#    2. Changing `#include <cblas.h>` to `#include <openblas/cblas.h>` doesn't work.
#    3. [OpenBLAS conditionally produces the OPENBLAS_INCLUDE_DIR](https://github.com/xianyi/OpenBLAS/blob/974acb39ff86121a5a94be4853f58bd728b56b81/cmake/OpenBLASConfig.cmake.in#L66-L78), but not in our case.  (I've tried logging them.)
#    4. The Windows build of Whisper just [copies the headers](https://github.com/ggerganov/whisper.cpp/blob/85c9ac18b59125b988cda40f40d8687e1ba88a7a/.github/workflows/build.yml#L148-L149) into the repo root.
find_library(OPENBLAS_LIB
  NAMES openblas libopenblas
)
if (OPENBLAS_LIB)
  cmake_path(REMOVE_FILENAME OPENBLAS_LIB OUTPUT_VARIABLE OPENBLAS_INCLUDE_DIR)
  cmake_path(APPEND OPENBLAS_INCLUDE_DIR ".." ".." "include" "openblas")
  cmake_path(NORMAL_PATH OPENBLAS_INCLUDE_DIR)
  target_include_directories(whisper PRIVATE "${OPENBLAS_INCLUDE_DIR}")
endif()
# End workaround for making OpenBLAS headers available to Whisper.

add_subdirectory(src/dotnet/runtime)
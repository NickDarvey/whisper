cmake_minimum_required(VERSION 3.20)

project(Whisper VERSION 1.0 LANGUAGES CXX)

# Bundle the whisper library inside the wrapper library
set(BUILD_SHARED_LIBS OFF)

add_subdirectory(ext/whisper.cpp)
add_subdirectory(src/dotnet/runtime)
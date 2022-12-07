add_library(dotnet SHARED ${SRC_DOTNET_WRAPPER_FILE_NAME})
set_target_properties(dotnet PROPERTIES
  OUTPUT_NAME ${SRC_DOTNET_LIBRARY_FILE_NAME}
)
target_link_libraries(dotnet PRIVATE
  whisper # defined by ../../ext/whisper.cpp/CMakeLists.txt
)
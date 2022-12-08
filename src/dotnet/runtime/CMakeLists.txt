add_library(dotnet SHARED ${DOTNET_WRAPPER_FILE_NAME})
set_target_properties(dotnet PROPERTIES
  OUTPUT_NAME ${DOTNET_LIBRARY_NAME}
)
target_link_libraries(dotnet PRIVATE
  whisper # defined by ../../ext/whisper.cpp/CMakeLists.txt
)
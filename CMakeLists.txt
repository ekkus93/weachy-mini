cmake_minimum_required(VERSION 3.25)
project(weachy_mini LANGUAGES C CXX)

include(CTest)

option(REACHY_ENABLE_SANITIZERS "Enable native ASan/UBSan in supported desktop builds" OFF)
option(REACHY_BUILD_LLAMA_RUNTIME "Build the pinned RMA-130 llama.cpp runtime plug-in" OFF)

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/native/cmake")
include(CompilerWarnings)

add_subdirectory(native/reachy_sim)

if(REACHY_BUILD_LLAMA_RUNTIME)
    add_subdirectory(native/llama_runtime)
endif()

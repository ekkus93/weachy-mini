cmake_minimum_required(VERSION 3.25)

project(weachy_mini LANGUAGES C)

include(CTest)

option(REACHY_ENABLE_SANITIZERS "Enable address and undefined-behavior sanitizers" OFF)

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/native/cmake")
include(CompilerWarnings)

add_subdirectory(native/reachy_sim)

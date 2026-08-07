if(NOT REACHY_BUILD_LLAMA_RUNTIME)
    return()
endif()

if(NOT DEFINED REACHY_LLAMA_CPP_SOURCE_DIR OR REACHY_LLAMA_CPP_SOURCE_DIR STREQUAL "")
    message(FATAL_ERROR "REACHY_LLAMA_CPP_SOURCE_DIR must identify the pinned llama.cpp checkout.")
endif()
if(NOT EXISTS "${REACHY_LLAMA_CPP_SOURCE_DIR}/include/llama.h")
    message(FATAL_ERROR "Pinned llama.cpp headers are missing from REACHY_LLAMA_CPP_SOURCE_DIR.")
endif()

# Third-party warning policy is intentionally isolated from first-party warnings.
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(LLAMA_ALL_WARNINGS OFF CACHE BOOL "" FORCE)
set(LLAMA_ALL_WARNINGS_3RD_PARTY OFF CACHE BOOL "" FORCE)
set(LLAMA_FATAL_WARNINGS OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_COMMON OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_SERVER OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_APP OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_UI OFF CACHE BOOL "" FORCE)
set(LLAMA_TOOLS_INSTALL OFF CACHE BOOL "" FORCE)
set(LLAMA_TESTS_INSTALL OFF CACHE BOOL "" FORCE)
set(LLAMA_OPENSSL OFF CACHE BOOL "" FORCE)
set(LLAMA_SUBPROCESS OFF CACHE BOOL "" FORCE)
set(GGML_NATIVE OFF CACHE BOOL "" FORCE)
set(GGML_OPENMP OFF CACHE BOOL "" FORCE)
set(GGML_LLAMAFILE OFF CACHE BOOL "" FORCE)
set(GGML_BACKEND_DL OFF CACHE BOOL "" FORCE)
set(GGML_RPC OFF CACHE BOOL "" FORCE)
if(ANDROID)
    set(GGML_CPU_ARM_ARCH "armv8-a" CACHE STRING "" FORCE)
endif()

# SYSTEM applies only to the imported upstream subtree. It keeps declarations in
# llama.cpp/ggml headers from being judged by Weachy's first-party -Werror policy.
add_subdirectory(
    "${REACHY_LLAMA_CPP_SOURCE_DIR}"
    "${CMAKE_CURRENT_BINARY_DIR}/upstream-llama-cpp"
    EXCLUDE_FROM_ALL
    SYSTEM
)

find_package(Threads REQUIRED)

add_library(reachy_llama SHARED src/reachy_llama.cpp)
target_compile_features(reachy_llama PRIVATE cxx_std_17)
target_include_directories(
    reachy_llama
    PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include"
    PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src"
)
target_link_libraries(reachy_llama PRIVATE llama Threads::Threads)
set_target_properties(
    reachy_llama
    PROPERTIES
        CXX_VISIBILITY_PRESET hidden
        VISIBILITY_INLINES_HIDDEN YES
        OUTPUT_NAME reachy_llama
)
reachy_enable_strict_warnings(reachy_llama)
reachy_enable_sanitizers(reachy_llama)

if(UNIX AND NOT APPLE)
    target_link_options(
        reachy_llama
        PRIVATE "-Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/reachy_llama.exports.map"
    )
endif()

if(BUILD_TESTING AND NOT ANDROID)
    add_executable(
        reachy_llama_contract_tests
        tests/reachy_llama_contract_tests.cpp
    )
    target_compile_features(reachy_llama_contract_tests PRIVATE cxx_std_17)
    target_include_directories(
        reachy_llama_contract_tests
        PRIVATE
            "${CMAKE_CURRENT_SOURCE_DIR}/include"
            "${CMAKE_CURRENT_SOURCE_DIR}/src"
    )
    target_link_libraries(reachy_llama_contract_tests PRIVATE reachy_llama Threads::Threads)
    reachy_enable_strict_warnings(reachy_llama_contract_tests)
    reachy_enable_sanitizers(reachy_llama_contract_tests)
    add_test(NAME reachy_llama_contracts COMMAND reachy_llama_contract_tests)
endif()

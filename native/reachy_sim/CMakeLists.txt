option(
    REACHY_BUILD_MUJOCO_BACKEND
    "Build the production reachy_sim backend against an external MuJoCo library"
    OFF
)
option(
    REACHY_BUILD_MUJOCO_PROBE
    "Build the Android MuJoCo feasibility executables against an external MuJoCo library"
    OFF
)
set(
    REACHY_MUJOCO_INCLUDE_DIR
    ""
    CACHE PATH
    "Directory containing mujoco/mujoco.h"
)
set(
    REACHY_MUJOCO_LIBRARY
    ""
    CACHE FILEPATH
    "Path to the external MuJoCo shared library"
)
set(
    REACHY_MUJOCO_EXPECTED_VERSION
    "3.9.0"
    CACHE STRING
    "Exact MuJoCo runtime version accepted by the production backend"
)

if(REACHY_BUILD_MUJOCO_BACKEND OR REACHY_BUILD_MUJOCO_PROBE)
    if(NOT EXISTS "${REACHY_MUJOCO_INCLUDE_DIR}/mujoco/mujoco.h")
        message(
            FATAL_ERROR
            "REACHY_MUJOCO_INCLUDE_DIR must contain mujoco/mujoco.h"
        )
    endif()
    if(NOT EXISTS "${REACHY_MUJOCO_LIBRARY}")
        message(
            FATAL_ERROR
            "REACHY_MUJOCO_LIBRARY must name the built MuJoCo shared library"
        )
    endif()
    if(NOT TARGET reachy_external_mujoco)
        add_library(reachy_external_mujoco SHARED IMPORTED GLOBAL)
        set_target_properties(
            reachy_external_mujoco
            PROPERTIES
                IMPORTED_LOCATION "${REACHY_MUJOCO_LIBRARY}"
                INTERFACE_INCLUDE_DIRECTORIES "${REACHY_MUJOCO_INCLUDE_DIR}"
        )
    endif()
endif()

set(REACHY_SIM_BACKEND_SOURCE src/reachy_sim_backend_unavailable.c)
if(REACHY_BUILD_MUJOCO_BACKEND)
    set(REACHY_SIM_BACKEND_SOURCE src/reachy_sim_backend_mujoco.c)
endif()

function(reachy_configure_sim_library target_name library_type)
    add_library(${target_name} ${library_type})
    target_sources(
        ${target_name}
        PRIVATE
            src/reachy_sim.c
            ${REACHY_SIM_BACKEND_SOURCE}
            src/reachy_sim_version.c
        PUBLIC
            FILE_SET public_headers
            TYPE HEADERS
            BASE_DIRS include
            FILES
                include/reachy_sim.h
                include/reachy_sim_version.h
    )
    target_include_directories(
        ${target_name}
        PRIVATE
            src
    )
    target_compile_features(${target_name} PUBLIC c_std_17)
    set_target_properties(${target_name} PROPERTIES C_EXTENSIONS OFF)
    reachy_enable_strict_warnings(${target_name})
    reachy_enable_sanitizers(${target_name})

    if(REACHY_BUILD_MUJOCO_BACKEND)
        target_compile_definitions(
            ${target_name}
            PRIVATE
                REACHY_SIM_HAS_MUJOCO_BACKEND=1
                REACHY_MUJOCO_EXPECTED_VERSION="${REACHY_MUJOCO_EXPECTED_VERSION}"
        )
        target_link_libraries(
            ${target_name}
            PRIVATE
                reachy_external_mujoco
        )
        if(NOT MSVC)
            target_link_libraries(${target_name} PRIVATE m)
        endif()
    endif()
endfunction()

reachy_configure_sim_library(reachy_sim STATIC)


add_library(reachy_servo_model STATIC)
target_sources(
    reachy_servo_model
    PRIVATE
        src/reachy_servo_model.cpp
        src/reachy_servo_parameters.generated.hpp
    PUBLIC
        FILE_SET public_headers
        TYPE HEADERS
        BASE_DIRS include
        FILES
  include/reachy_servo_model.hpp
)
target_include_directories(
    reachy_servo_model
    PRIVATE
        src
)
target_compile_features(reachy_servo_model PUBLIC cxx_std_17)
set_target_properties(reachy_servo_model PROPERTIES CXX_EXTENSIONS OFF)
reachy_enable_strict_warnings(reachy_servo_model)
reachy_enable_sanitizers(reachy_servo_model)

if(REACHY_BUILD_MUJOCO_BACKEND)
    reachy_configure_sim_library(reachy_sim_shared SHARED)
    target_compile_definitions(
        reachy_sim_shared
        PRIVATE
            REACHY_SIM_BUILD_SHARED
    )
    set_target_properties(
        reachy_sim_shared
        PROPERTIES
            OUTPUT_NAME reachy_sim
            ARCHIVE_OUTPUT_NAME reachy_sim_production
            LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/reachy-production"
            RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/reachy-production"
    )
endif()

if(BUILD_TESTING)
    add_executable(
        reachy_servo_model_test
        tests/reachy_servo_model_test.cpp
    )
    target_link_libraries(
        reachy_servo_model_test
        PRIVATE
  reachy_servo_model
    )
    target_compile_features(
        reachy_servo_model_test
        PRIVATE
  cxx_std_17
    )
    set_target_properties(
        reachy_servo_model_test
        PROPERTIES
  CXX_EXTENSIONS OFF
    )
    reachy_enable_strict_warnings(reachy_servo_model_test)
    reachy_enable_sanitizers(reachy_servo_model_test)
    add_test(
        NAME reachy_servo_model_test
        COMMAND reachy_servo_model_test
    )

    add_executable(
        reachy_sim_version_test
        tests/reachy_sim_version_test.c
    )
    target_link_libraries(
        reachy_sim_version_test
        PRIVATE
            reachy_sim
    )
    target_compile_features(
        reachy_sim_version_test
        PRIVATE
            c_std_17
    )
    set_target_properties(
        reachy_sim_version_test
        PROPERTIES
            C_EXTENSIONS OFF
    )
    reachy_enable_strict_warnings(reachy_sim_version_test)
    reachy_enable_sanitizers(reachy_sim_version_test)
    add_test(
        NAME reachy_sim_version_test
        COMMAND reachy_sim_version_test
    )

    if(NOT REACHY_BUILD_MUJOCO_BACKEND)
        add_executable(
            reachy_sim_backend_unavailable_test
            tests/reachy_sim_backend_unavailable_test.c
        )
        target_link_libraries(
            reachy_sim_backend_unavailable_test
            PRIVATE
                reachy_sim
        )
        target_compile_features(
            reachy_sim_backend_unavailable_test
            PRIVATE
                c_std_17
        )
        set_target_properties(
            reachy_sim_backend_unavailable_test
            PROPERTIES
                C_EXTENSIONS OFF
        )
        reachy_enable_strict_warnings(
            reachy_sim_backend_unavailable_test
        )
        reachy_enable_sanitizers(
            reachy_sim_backend_unavailable_test
        )
        add_test(
            NAME reachy_sim_backend_unavailable_test
            COMMAND reachy_sim_backend_unavailable_test
        )
    endif()

    add_executable(
        reachy_sim_mujoco_backend_test
        src/reachy_sim_backend_mujoco.c
        tests/fake_mujoco/src/fake_mujoco.c
        tests/reachy_sim_mujoco_backend_test.c
    )
    target_include_directories(
        reachy_sim_mujoco_backend_test
        PRIVATE
            tests/fake_mujoco/include
            include
            src
    )
    target_compile_definitions(
        reachy_sim_mujoco_backend_test
        PRIVATE
            REACHY_MUJOCO_EXPECTED_VERSION="${REACHY_MUJOCO_EXPECTED_VERSION}"
    )
    target_compile_features(
        reachy_sim_mujoco_backend_test
        PRIVATE
            c_std_17
    )
    set_target_properties(
        reachy_sim_mujoco_backend_test
        PROPERTIES
            C_EXTENSIONS OFF
    )
    reachy_enable_strict_warnings(reachy_sim_mujoco_backend_test)
    reachy_enable_sanitizers(reachy_sim_mujoco_backend_test)
    if(NOT MSVC)
        target_link_libraries(reachy_sim_mujoco_backend_test PRIVATE m)
    endif()
    add_test(
        NAME reachy_sim_mujoco_backend_test
        COMMAND reachy_sim_mujoco_backend_test
    )

    add_library(reachy_sim_contract STATIC)
    target_sources(
        reachy_sim_contract
        PRIVATE
            src/reachy_sim.c
            src/reachy_sim_version.c
            tests/reachy_sim_fake_backend.c
        PUBLIC
            FILE_SET public_headers
            TYPE HEADERS
            BASE_DIRS include
            FILES
                include/reachy_sim.h
                include/reachy_sim_version.h
    )
    target_include_directories(
        reachy_sim_contract
        PRIVATE
            src
    )
    target_compile_features(reachy_sim_contract PUBLIC c_std_17)
    set_target_properties(
        reachy_sim_contract
        PROPERTIES
            C_EXTENSIONS OFF
    )
    reachy_enable_strict_warnings(reachy_sim_contract)
    reachy_enable_sanitizers(reachy_sim_contract)
    if(NOT MSVC)
        target_link_libraries(reachy_sim_contract PRIVATE m)
    endif()

    add_library(reachy_sim_managed_contract SHARED)
    target_sources(
        reachy_sim_managed_contract
        PRIVATE
            src/reachy_sim.c
            src/reachy_sim_version.c
            tests/reachy_sim_fake_backend.c
    )
    target_include_directories(
        reachy_sim_managed_contract
        PUBLIC
            include
        PRIVATE
            src
    )
    target_compile_features(
        reachy_sim_managed_contract
        PUBLIC
            c_std_17
    )
    set_target_properties(
        reachy_sim_managed_contract
        PROPERTIES
            C_EXTENSIONS OFF
            OUTPUT_NAME reachy_sim
            ARCHIVE_OUTPUT_NAME reachy_sim_managed_contract
    )
    reachy_enable_strict_warnings(
        reachy_sim_managed_contract
    )
    reachy_enable_sanitizers(
        reachy_sim_managed_contract
    )
    if(NOT MSVC)
        target_link_libraries(
            reachy_sim_managed_contract
            PRIVATE
                m
        )
    endif()

    add_executable(
        reachy_sim_contract_test
        tests/reachy_sim_contract_test.c
    )
    target_link_libraries(
        reachy_sim_contract_test
        PRIVATE
            reachy_sim_contract
    )
    target_compile_features(
        reachy_sim_contract_test
        PRIVATE
            c_std_17
    )
    set_target_properties(
        reachy_sim_contract_test
        PROPERTIES
            C_EXTENSIONS OFF
    )
    reachy_enable_strict_warnings(reachy_sim_contract_test)
    reachy_enable_sanitizers(reachy_sim_contract_test)
    add_test(
        NAME reachy_sim_contract_test
        COMMAND reachy_sim_contract_test
    )

    add_executable(
        reachy_sim_header_cpp_test
        tests/reachy_sim_header_cpp_test.cpp
    )
    target_link_libraries(
        reachy_sim_header_cpp_test
        PRIVATE
            reachy_sim_contract
    )
    target_compile_features(
        reachy_sim_header_cpp_test
        PRIVATE
            cxx_std_17
    )
    set_target_properties(
        reachy_sim_header_cpp_test
        PROPERTIES
            CXX_EXTENSIONS OFF
    )
    reachy_enable_strict_warnings(reachy_sim_header_cpp_test)
    reachy_enable_sanitizers(reachy_sim_header_cpp_test)
    add_test(
        NAME reachy_sim_header_cpp_test
        COMMAND reachy_sim_header_cpp_test
    )
endif()

add_subdirectory(feasibility)

include("${CMAKE_CURRENT_LIST_DIR}/electrical_servo_model.cmake")

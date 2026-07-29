if(BUILD_TESTING)
    add_library(reachy_fake_mujoco STATIC)
    target_sources(
        reachy_fake_mujoco
        PRIVATE
            ../tests/fake_mujoco/src/fake_mujoco.c
    )
    target_include_directories(
        reachy_fake_mujoco
        PUBLIC
            ../tests/fake_mujoco/include
    )
    target_compile_features(reachy_fake_mujoco PUBLIC c_std_17)
    set_target_properties(reachy_fake_mujoco PROPERTIES C_EXTENSIONS OFF)
    reachy_enable_strict_warnings(reachy_fake_mujoco)
    reachy_enable_sanitizers(reachy_fake_mujoco)

    add_library(reachy_mujoco_probe_contract STATIC)
    target_sources(
        reachy_mujoco_probe_contract
        PRIVATE
            reachy_mujoco_probe.c
        PUBLIC
            FILE_SET public_headers
            TYPE HEADERS
            BASE_DIRS .
            FILES reachy_mujoco_probe.h
    )
    target_link_libraries(reachy_mujoco_probe_contract PRIVATE reachy_fake_mujoco m)
    target_compile_features(reachy_mujoco_probe_contract PUBLIC c_std_17)
    set_target_properties(reachy_mujoco_probe_contract PROPERTIES C_EXTENSIONS OFF)
    reachy_enable_strict_warnings(reachy_mujoco_probe_contract)
    reachy_enable_sanitizers(reachy_mujoco_probe_contract)

    add_executable(reachy_mujoco_probe_contract_test)
    target_sources(
        reachy_mujoco_probe_contract_test
        PRIVATE
            ../tests/reachy_mujoco_probe_contract_test.c
    )
    target_link_libraries(
        reachy_mujoco_probe_contract_test
        PRIVATE
            reachy_mujoco_probe_contract
            reachy_fake_mujoco
            m
    )
    target_compile_features(reachy_mujoco_probe_contract_test PRIVATE c_std_17)
    set_target_properties(reachy_mujoco_probe_contract_test PROPERTIES C_EXTENSIONS OFF)
    reachy_enable_strict_warnings(reachy_mujoco_probe_contract_test)
    reachy_enable_sanitizers(reachy_mujoco_probe_contract_test)
    add_test(NAME reachy_mujoco_probe_contract_test COMMAND reachy_mujoco_probe_contract_test)
endif()

option(REACHY_BUILD_MUJOCO_PROBE "Build the probe against an external MuJoCo library" OFF)
set(REACHY_MUJOCO_INCLUDE_DIR "" CACHE PATH "Directory containing mujoco/mujoco.h")
set(REACHY_MUJOCO_LIBRARY "" CACHE FILEPATH "Path to libmujoco")

if(REACHY_BUILD_MUJOCO_PROBE)
    if(NOT EXISTS "${REACHY_MUJOCO_INCLUDE_DIR}/mujoco/mujoco.h")
        message(FATAL_ERROR "REACHY_MUJOCO_INCLUDE_DIR does not contain mujoco/mujoco.h")
    endif()
    if(NOT EXISTS "${REACHY_MUJOCO_LIBRARY}")
        message(FATAL_ERROR "REACHY_MUJOCO_LIBRARY does not exist")
    endif()

    add_library(reachy_external_mujoco SHARED IMPORTED GLOBAL)
    set_target_properties(
        reachy_external_mujoco
        PROPERTIES
            IMPORTED_LOCATION "${REACHY_MUJOCO_LIBRARY}"
            INTERFACE_INCLUDE_DIRECTORIES "${REACHY_MUJOCO_INCLUDE_DIR}"
    )

    add_library(reachy_mujoco_probe STATIC)
    target_sources(
        reachy_mujoco_probe
        PRIVATE
            reachy_mujoco_probe.c
        PUBLIC
            FILE_SET public_headers
            TYPE HEADERS
            BASE_DIRS .
            FILES reachy_mujoco_probe.h
    )
    target_link_libraries(reachy_mujoco_probe PRIVATE reachy_external_mujoco m)
    target_compile_features(reachy_mujoco_probe PUBLIC c_std_17)
    set_target_properties(reachy_mujoco_probe PROPERTIES C_EXTENSIONS OFF)
    reachy_enable_strict_warnings(reachy_mujoco_probe)

    add_executable(reachy_mujoco_probe_runner reachy_mujoco_probe_runner.c)
    target_link_libraries(
        reachy_mujoco_probe_runner
        PRIVATE
            reachy_mujoco_probe
            reachy_external_mujoco
            m
    )
    target_compile_features(reachy_mujoco_probe_runner PRIVATE c_std_17)
    set_target_properties(reachy_mujoco_probe_runner PROPERTIES C_EXTENSIONS OFF)
    reachy_enable_strict_warnings(reachy_mujoco_probe_runner)
endif()

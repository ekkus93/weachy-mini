add_library(reachy_sim STATIC)

target_sources(
    reachy_sim
    PRIVATE
        src/reachy_sim.c
        src/reachy_sim_backend_unavailable.c
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
    reachy_sim
    PRIVATE
        src
)
target_compile_features(reachy_sim PUBLIC c_std_17)
set_target_properties(reachy_sim PROPERTIES C_EXTENSIONS OFF)
reachy_enable_strict_warnings(reachy_sim)
reachy_enable_sanitizers(reachy_sim)

if(BUILD_TESTING)
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

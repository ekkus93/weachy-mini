add_library(reachy_sim_scaffold STATIC)

target_sources(
    reachy_sim_scaffold
    PRIVATE
        src/reachy_sim_version.c
    PUBLIC
        FILE_SET public_headers
        TYPE HEADERS
        BASE_DIRS include
        FILES include/reachy_sim_version.h
)

target_compile_features(reachy_sim_scaffold PUBLIC c_std_17)
set_target_properties(reachy_sim_scaffold PROPERTIES C_EXTENSIONS OFF)
reachy_enable_strict_warnings(reachy_sim_scaffold)
reachy_enable_sanitizers(reachy_sim_scaffold)

if(BUILD_TESTING)
    add_executable(reachy_sim_version_test tests/reachy_sim_version_test.c)
    target_link_libraries(reachy_sim_version_test PRIVATE reachy_sim_scaffold)
    target_compile_features(reachy_sim_version_test PRIVATE c_std_17)
    set_target_properties(reachy_sim_version_test PROPERTIES C_EXTENSIONS OFF)
    reachy_enable_strict_warnings(reachy_sim_version_test)
    reachy_enable_sanitizers(reachy_sim_version_test)
    add_test(NAME reachy_sim_version_test COMMAND reachy_sim_version_test)
endif()

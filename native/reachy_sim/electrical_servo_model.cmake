target_sources(
    reachy_servo_model
    PRIVATE
        src/reachy_electrical_servo_model.cpp
        src/reachy_electrical_baseline.generated.hpp
    PUBLIC
        FILE_SET public_headers
        TYPE HEADERS
        BASE_DIRS include
        FILES
            include/reachy_electrical_servo_model.hpp
)

if(BUILD_TESTING)
    add_executable(
        reachy_electrical_servo_model_test
        tests/reachy_electrical_servo_model_test.cpp
    )
    target_link_libraries(
        reachy_electrical_servo_model_test
        PRIVATE
            reachy_servo_model
    )
    target_compile_features(
        reachy_electrical_servo_model_test
        PRIVATE
            cxx_std_17
    )
    set_target_properties(
        reachy_electrical_servo_model_test
        PROPERTIES
            CXX_EXTENSIONS OFF
    )
    reachy_enable_strict_warnings(reachy_electrical_servo_model_test)
    reachy_enable_sanitizers(reachy_electrical_servo_model_test)
    add_test(
        NAME reachy_electrical_servo_model_test
        COMMAND reachy_electrical_servo_model_test
    )
endif()

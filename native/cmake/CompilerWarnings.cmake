function(reachy_enable_strict_warnings target_name)
    if(MSVC)
        target_compile_options(${target_name} PRIVATE /W4 /WX /permissive-)
    elseif(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(
            ${target_name}
            PRIVATE
                -Wall
                -Wextra
                -Wpedantic
                -Wconversion
                -Wsign-conversion
                -Wshadow
                -Werror
        )
    else()
        message(FATAL_ERROR "Unsupported compiler for strict first-party warnings: ${CMAKE_C_COMPILER_ID}")
    endif()
endfunction()

function(reachy_enable_sanitizers target_name)
    if(NOT REACHY_ENABLE_SANITIZERS)
        return()
    endif()

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU" AND NOT ANDROID)
        target_compile_options(${target_name} PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
        target_link_options(${target_name} PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
    else()
        message(FATAL_ERROR "Sanitizers are enabled only for supported desktop Clang/GNU builds.")
    endif()
endfunction()

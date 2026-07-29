if(NOT ANDROID)
  message(FATAL_ERROR "Reachy MuJoCo Android compatibility injection requires an Android toolchain.")
endif()

if(NOT DEFINED REACHY_MUJOCO_ANDROID_COMPAT_SOURCE OR
   NOT DEFINED REACHY_MUJOCO_ANDROID_COMPAT_HEADER)
  message(FATAL_ERROR "Reachy MuJoCo Android compatibility source/header paths are required.")
endif()

get_property(
  reachy_android_api26_scheduled
  GLOBAL
  PROPERTY REACHY_ANDROID_API26_COMPAT_SCHEDULED
)
if(NOT reachy_android_api26_scheduled)
  set_property(GLOBAL PROPERTY REACHY_ANDROID_API26_COMPAT_SCHEDULED TRUE)

  function(reachy_apply_android_api26_compat)
    if(NOT TARGET mujoco)
      message(FATAL_ERROR "MuJoCo target was not created before Android compatibility injection.")
    endif()
    if(NOT EXISTS "${REACHY_MUJOCO_ANDROID_COMPAT_SOURCE}")
      message(FATAL_ERROR "Android compatibility source does not exist: ${REACHY_MUJOCO_ANDROID_COMPAT_SOURCE}")
    endif()
    if(NOT EXISTS "${REACHY_MUJOCO_ANDROID_COMPAT_HEADER}")
      message(FATAL_ERROR "Android compatibility header does not exist: ${REACHY_MUJOCO_ANDROID_COMPAT_HEADER}")
    endif()

    target_sources(
      mujoco
      PRIVATE "${REACHY_MUJOCO_ANDROID_COMPAT_SOURCE}"
    )
    target_compile_definitions(
      mujoco
      PRIVATE _POSIX_C_SOURCE=200809L
    )
    target_compile_options(
      mujoco
      PRIVATE
        "$<$<COMPILE_LANGUAGE:C>:-include>"
        "$<$<COMPILE_LANGUAGE:C>:${REACHY_MUJOCO_ANDROID_COMPAT_HEADER}>"
    )
    message(STATUS "Applied first-party Android API 26 compatibility shim to MuJoCo.")
  endfunction()

  cmake_language(DEFER CALL reachy_apply_android_api26_compat)
endif()

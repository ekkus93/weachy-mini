# shellcheck shell=bash
#
# Device/ADB interaction helpers for run_rma092_camera_texture_acceptance_android.sh.
#
# Split out of that script (docs/LARGE_FILE_REFACTOR_TODO_3.md-style refactor,
# performed after round 3 closed) to keep the entrypoint under 800 lines. This
# file is meant to be `source`d, not executed directly -- it only defines
# functions and relies on globals (ADB, ADB_BIN, REPORT_DIR, PACKAGE_NAME,
# REMOTE_FILES_DIR, REMOTE_ACQUISITION_RESULT, REMOTE_ACQUISITION_COMMAND,
# REMOTE_TEXTURE_RESULT, STAGE_MARKER_GLOB, REAR_TEXTURE_FILE,
# ROTATED_TEXTURE_FILE, FRONT_TEXTURE_FILE, FOREGROUND_HELPER, DEVICE_SERIAL,
# LAUNCH_COMPONENT, ORIGINAL_ACCELEROMETER_ROTATION, ORIGINAL_USER_ROTATION)
# that the entrypoint defines before sourcing this file and before any of
# these functions are actually called.

select_device_serial()
{
    mapfile -t serials < <(
        "${ADB_BIN}" devices \
            | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )
    local -a accepted=()
    local serial
    for serial in "${serials[@]}"; do
        local abi sdk
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
        sdk="$("${ADB_BIN}" -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\r')"
        if [[ "${abi}" == "arm64-v8a" && "${sdk}" =~ ^[0-9]+$ ]] && (( sdk >= 26 )); then
            accepted+=("${serial}")
        fi
    done
    if (( ${#accepted[@]} != 1 )); then
        printf 'Expected exactly one physical arm64-v8a API-26+ device; found %s.\n' \
            "${#accepted[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi
    printf '%s\n' "${accepted[0]}"
}

read_remote_file()
{
    local path="$1"
    "${ADB[@]}" shell \
        "if test -f '${path}'; then cat '${path}'; fi" \
        2>/dev/null \
        | tr -d '\r' \
        || true
}

read_stage_marker()
{
    "${ADB[@]}" shell \
        "marker=\$(ls -1t '${REMOTE_FILES_DIR}'/${STAGE_MARKER_GLOB} 2>/dev/null | head -n 1); if test -n \"\${marker}\"; then basename \"\${marker}\"; cat \"\${marker}\"; fi" \
        2>/dev/null \
        | tr -d '\r' \
        || true
}

capture_diagnostics()
{
    set +e
    "${ADB[@]}" logcat -d -v threadtime > "${REPORT_DIR}/logcat.txt"
    "${ADB[@]}" shell dumpsys activity activities > "${REPORT_DIR}/activity.txt"
    "${ADB[@]}" shell dumpsys window windows > "${REPORT_DIR}/window.txt"
    "${ADB[@]}" shell dumpsys display > "${REPORT_DIR}/display.txt"
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt"
    "${ADB[@]}" shell \
        "ls -laR '${REMOTE_FILES_DIR}' 2>&1" \
        > "${REPORT_DIR}/external-files.txt"
    read_remote_file "${REMOTE_ACQUISITION_RESULT}" \
        > "${REPORT_DIR}/acquisition-latest.json"
    read_remote_file "${REMOTE_TEXTURE_RESULT}" \
        > "${REPORT_DIR}/texture-latest.json"
    read_stage_marker > "${REPORT_DIR}/stage-latest.txt"
    "${ADB[@]}" exec-out screencap -p \
        > "${REPORT_DIR}/device-screen-final.png"
}

cleanup()
{
    local exit_code=$?
    trap - EXIT
    if (( exit_code != 0 )); then
        capture_diagnostics
    fi
    set +e
    if [[ "${ORIGINAL_ACCELEROMETER_ROTATION}" =~ ^[01]$ ]]; then
        "${ADB[@]}" shell settings put system accelerometer_rotation \
            "${ORIGINAL_ACCELEROMETER_ROTATION}" >/dev/null 2>&1
    fi
    if [[ "${ORIGINAL_USER_ROTATION}" =~ ^[0-3]$ ]]; then
        "${ADB[@]}" shell settings put system user_rotation \
            "${ORIGINAL_USER_ROTATION}" >/dev/null 2>&1
    fi
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1
    "${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA \
        >/dev/null 2>&1
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10 \
        >/dev/null 2>&1
    exit "${exit_code}"
}

remove_remote_evidence()
{
    "${ADB[@]}" shell rm -f \
        "${REMOTE_ACQUISITION_RESULT}" \
        "${REMOTE_ACQUISITION_RESULT}.tmp" \
        "${REMOTE_ACQUISITION_COMMAND}" \
        "${REMOTE_ACQUISITION_COMMAND}.tmp" \
        "${REMOTE_TEXTURE_RESULT}" \
        "${REMOTE_TEXTURE_RESULT}.tmp" \
        "${REMOTE_FILES_DIR}/${REAR_TEXTURE_FILE}" \
        "${REMOTE_FILES_DIR}/${REAR_TEXTURE_FILE}.tmp" \
        "${REMOTE_FILES_DIR}/${ROTATED_TEXTURE_FILE}" \
        "${REMOTE_FILES_DIR}/${ROTATED_TEXTURE_FILE}.tmp" \
        "${REMOTE_FILES_DIR}/${FRONT_TEXTURE_FILE}" \
        "${REMOTE_FILES_DIR}/${FRONT_TEXTURE_FILE}.tmp" \
        >/dev/null 2>&1 || true
    "${ADB[@]}" shell \
        "find '${REMOTE_FILES_DIR}' -maxdepth 1 -type f -name '${STAGE_MARKER_GLOB}' -delete" \
        >/dev/null 2>&1 || true
}

launch_application()
{
    local suffix="$1"
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
        > "${REPORT_DIR}/prepare-${suffix}.txt"
    "${ADB[@]}" shell am start -W \
        -n "${LAUNCH_COMPONENT}" \
        -a android.intent.action.MAIN \
        -c android.intent.category.LAUNCHER \
        --ez reachy_rma091_acceptance true \
        --ez reachy_rma092_acceptance true \
        > "${REPORT_DIR}/launch-${suffix}.txt"
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30 \
        > "${REPORT_DIR}/focus-${suffix}.txt"
}

pull_remote_evidence()
{
    local remote_name="$1"
    local local_name="$2"
    "${ADB[@]}" pull \
        "${REMOTE_FILES_DIR}/${remote_name}" \
        "${REPORT_DIR}/${local_name}" \
        > "${REPORT_DIR}/pull-${local_name}.txt"
    test -s "${REPORT_DIR}/${local_name}"
}

capture_stage_screen()
{
    local local_name="$1"
    "${ADB[@]}" exec-out screencap -p > "${REPORT_DIR}/${local_name}"
    test -s "${REPORT_DIR}/${local_name}"
}

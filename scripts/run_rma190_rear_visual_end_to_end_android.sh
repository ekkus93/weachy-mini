#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA190_REAR_VISUAL_REPORT_DIR:-${ROOT_DIR}/build/rma190-rear-visual-end-to-end-report}"
PACKAGE_NAME="com.ekkus.weachymini"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
RESULT_FILE="rma190-rear-visual-end-to-end-acceptance.json"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT="${REMOTE_FILES_DIR}/${RESULT_FILE}"
TIMEOUT_SECONDS="${RMA190_REAR_VISUAL_TIMEOUT_SECONDS:-150}"
POLL_SECONDS="${RMA190_REAR_VISUAL_POLL_SECONDS:-0.5}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null
command -v python3 >/dev/null
command -v sha256sum >/dev/null
if [[ ! -s "${FOREGROUND_HELPER}" ]]; then
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
fi

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

DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:-$(select_device_serial)}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")
rm -rf -- "${REPORT_DIR}"
mkdir -p "${REPORT_DIR}"

read_report_run_as()
{
    "${ADB[@]}" exec-out run-as "${PACKAGE_NAME}" sh -c \
        "test -f '${REMOTE_RESULT}' && cat '${REMOTE_RESULT}'" \
        2>/dev/null | tr -d '\r' || true
}

read_report_shell()
{
    "${ADB[@]}" shell \
        "if test -f '${REMOTE_RESULT}'; then cat '${REMOTE_RESULT}'; fi" \
        2>/dev/null | tr -d '\r' || true
}

read_report()
{
    local value
    value="$(read_report_run_as)"
    if [[ -z "${value}" ]]; then
        value="$(read_report_shell)"
    fi
    printf '%s' "${value}"
}

capture_diagnostics()
{
    "${ADB[@]}" logcat -d -v threadtime > "${REPORT_DIR}/logcat.txt" || true
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
        > "${REPORT_DIR}/package.txt" || true
    "${ADB[@]}" shell dumpsys media.camera \
        > "${REPORT_DIR}/camera.txt" || true
    "${ADB[@]}" shell dumpsys activity activities \
        > "${REPORT_DIR}/activity.txt" || true
    read_report > "${REPORT_DIR}/device-report.json" || true
    "${ADB[@]}" exec-out screencap -p \
        > "${REPORT_DIR}/device-screen-final.png" || true
}

cleanup()
{
    local exit_code=$?
    trap - EXIT
    capture_diagnostics
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10 \
        >/dev/null 2>&1 || true
    exit "${exit_code}"
}
trap cleanup EXIT

"${ADB[@]}" install -r -g "${APK_PATH}" > "${REPORT_DIR}/install.txt"
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/pm-clear.txt"
"${ADB[@]}" shell pm grant "${PACKAGE_NAME}" android.permission.CAMERA

LAUNCH_COMPONENT="$(
    "${ADB[@]}" shell cmd package resolve-activity --brief \
        -a android.intent.action.MAIN \
        -c android.intent.category.LAUNCHER \
        "${PACKAGE_NAME}" \
        | tr -d '\r' \
        | tail -n 1
)"
if [[ -z "${LAUNCH_COMPONENT}" || "${LAUNCH_COMPONENT}" != */* ]]; then
    printf 'Could not resolve launch component for %s: %s\n' \
        "${PACKAGE_NAME}" "${LAUNCH_COMPONENT}" >&2
    exit 1
fi

"${ADB[@]}" shell mkdir -p "${REMOTE_FILES_DIR}"
"${ADB[@]}" shell rm -f "${REMOTE_RESULT}" "${REMOTE_RESULT}.tmp"
"${ADB[@]}" logcat -c
"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
    > "${REPORT_DIR}/prepare.txt"
"${ADB[@]}" shell am start -W \
    -n "${LAUNCH_COMPONENT}" \
    -a android.intent.action.MAIN \
    -c android.intent.category.LAUNCHER \
    --ez reachy_rma190_rear_visual_e2e_acceptance true \
    > "${REPORT_DIR}/launch.txt"
cat "${REPORT_DIR}/launch.txt"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30 \
    > "${REPORT_DIR}/focus.txt"

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
report_json=""
while true; do
    report_json="$(read_report)"
    if [[ "${report_json}" == \{* ]]; then
        break
    fi
    report_json=""
    if (( $(date +%s) >= deadline )); then
        printf 'Timed out waiting for RMA-190 rear-camera visual evidence.\n' >&2
        exit 1
    fi
    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf 'Unity exited before RMA-190 rear-camera visual evidence was written.\n' >&2
        exit 1
    fi
    sleep "${POLL_SECONDS}"
done

printf '%s\n' "${report_json}" > "${REPORT_DIR}/${RESULT_FILE}"
if ! python3 - "${REPORT_DIR}/${RESULT_FILE}" <<'PY'
from __future__ import annotations

import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
report = json.loads(path.read_text(encoding="utf-8"))
assert report["status"] == "passed", report
assert report["real_rear_camera"] is True, report
assert report["transformed_reachy_eye_frame"] is True, report
assert report["validity_mask_present"] is True, report
assert report["deterministic_calibration_fixture"] is True, report
assert report["measured_calibration_claimed"] is False, report
assert report["calibration_provenance"] == "UncalibratedEstimate", report
assert report["camera_id"], report
assert report["camera_session_id"] > 0, report
assert report["source_sequence"] > 0, report
assert report["authoritative_sequence"] > 0, report
assert report["continuity_id"] > 0, report
assert 0.0 <= float(report["coverage_fraction"]) <= 1.0, report
assert report["visual_question"], report
assert report["vlm_status"] in ("Succeeded", "Unavailable"), report
assert report["vlm_succeeded"] is True or report["vlm_optional_unavailable"] is True, report
if report["vlm_succeeded"]:
    assert report["vlm_response_characters"] > 0, report
assert report["synthetic_camera_frame_injected"] is False, report
assert report["cloud_credentials_injected"] is False, report
print(json.dumps(report, indent=2, sort_keys=True))
PY
then
    printf '%s\n' 'RMA-190 rear-camera visual report JSON validation failed.' >&2
    exit 1
fi

capture_diagnostics
sha256sum "${APK_PATH}" > "${REPORT_DIR}/apk.sha256"
sha256sum "${REPORT_DIR}/${RESULT_FILE}" > "${REPORT_DIR}/report.sha256"
{
    printf 'device_serial=%s\n' "${DEVICE_SERIAL}"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
} > "${REPORT_DIR}/environment.txt"
printf 'RMA-190 rear-camera transformed frame and optional VLM visual question passed.\n'

#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA154_VISUAL_SERVO_REPORT_DIR:-${ROOT_DIR}/build/rma154-visual-servo-report}"
PACKAGE_NAME="com.ekkus.weachymini"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
RESULT_FILE="rma154-visual-servo-state.json"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT="${REMOTE_FILES_DIR}/${RESULT_FILE}"
TIMEOUT_SECONDS="${RMA154_VISUAL_SERVO_TIMEOUT_SECONDS:-120}"
POLL_SECONDS="${RMA154_VISUAL_SERVO_POLL_SECONDS:-0.5}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'RMA-154 timeout must be a positive integer: %s\n' \
        "${TIMEOUT_SECONDS}" >&2
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

capture_diagnostics()
{
    set +e
    "${ADB[@]}" logcat -d -v threadtime > "${REPORT_DIR}/logcat.txt"
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt"
    "${ADB[@]}" shell dumpsys activity activities > "${REPORT_DIR}/activity.txt"
    "${ADB[@]}" shell \
        "ls -laR '${REMOTE_FILES_DIR}' 2>&1" \
        > "${REPORT_DIR}/external-files.txt"
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
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10 \
        >/dev/null 2>&1
    exit "${exit_code}"
}
trap cleanup EXIT

"${ADB[@]}" install -r -g "${APK_PATH}" \
    > "${REPORT_DIR}/install.txt"
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
    --ez reachy_rma154_acceptance true \
    > "${REPORT_DIR}/launch.txt"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30 \
    > "${REPORT_DIR}/focus.txt"

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
report_json=""
while true; do
    report_json="$(
        "${ADB[@]}" shell \
            "if test -f '${REMOTE_RESULT}'; then cat '${REMOTE_RESULT}'; fi" \
            2>/dev/null \
            | tr -d '\r' \
    )"
    if [[ -n "${report_json}" ]]; then
        break
    fi
    if (( $(date +%s) >= deadline )); then
        printf 'Timed out waiting for RMA-154 visual-servo evidence.\n' >&2
        exit 1
    fi
    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf 'Unity application exited before RMA-154 evidence was written.\n' >&2
        exit 1
    fi
    sleep "${POLL_SECONDS}"
done

printf '%s\n' "${report_json}" > "${REPORT_DIR}/${RESULT_FILE}"
python3 - "${REPORT_DIR}/${RESULT_FILE}" <<'PY'
from __future__ import annotations

import json
import math
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
report = json.loads(path.read_text(encoding="utf-8"))
if report.get("status") != "passed":
    raise SystemExit(f"RMA-154 acceptance did not pass: {report}")
for field in (
    "acceptance_enabled",
    "centered",
    "actual_motion_observed",
    "post_motion_frame_observed",
):
    if report.get(field) is not True:
        raise SystemExit(f"RMA-154 required truth field failed ({field}): {report}")
for field in (
    "requested_target_used_as_motion_proof",
    "raw_joint_command_used",
    "torque_command_used",
):
    if report.get(field) is not False:
        raise SystemExit(f"RMA-154 forbidden-path field failed ({field}): {report}")
initial_sequence = int(report.get("initial_authoritative_sequence", 0))
final_sequence = int(report.get("final_authoritative_sequence", 0))
if initial_sequence <= 0 or final_sequence <= initial_sequence:
    raise SystemExit(f"RMA-154 authoritative state did not advance: {report}")
initial_x = float(report.get("initial_center_x", math.nan))
initial_y = float(report.get("initial_center_y", math.nan))
final_x_error = float(report.get("final_horizontal_error", math.nan))
final_y_error = float(report.get("final_vertical_error", math.nan))
if not all(math.isfinite(v) for v in (initial_x, initial_y, final_x_error, final_y_error)):
    raise SystemExit(f"RMA-154 report has non-finite tracking values: {report}")
if abs(initial_x - 0.5) <= 0.06:
    raise SystemExit(f"RMA-154 target did not start near an image edge: {report}")
if abs(final_x_error) > 0.06 or abs(final_y_error) > 0.06:
    raise SystemExit(f"RMA-154 target did not finish inside tolerance: {report}")
if abs(final_x_error) >= abs(initial_x - 0.5):
    raise SystemExit(f"RMA-154 horizontal error did not improve: {report}")
if int(report.get("adjustment_count", 0)) < 1:
    raise SystemExit(f"RMA-154 issued no visual-servo adjustment: {report}")
if int(report.get("submitted_frame_count", 0)) < 1:
    raise SystemExit(f"RMA-154 submitted no bounded trajectory frames: {report}")
if int(report.get("transformed_frame_count", 0)) < 2:
    raise SystemExit(f"RMA-154 did not produce post-command transformed frames: {report}")
if float(report.get("maximum_authoritative_motion_radians", 0.0)) < 1.0e-5:
    raise SystemExit(f"RMA-154 observed insufficient physical motion: {report}")
PY

capture_diagnostics
sha256sum "${APK_PATH}" > "${REPORT_DIR}/apk.sha256"
sha256sum "${REPORT_DIR}/${RESULT_FILE}" > "${REPORT_DIR}/report.sha256"
printf 'device_serial=%s\n' "${DEVICE_SERIAL}" > "${REPORT_DIR}/environment.txt"
printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')" \
    >> "${REPORT_DIR}/environment.txt"
printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')" \
    >> "${REPORT_DIR}/environment.txt"
printf 'RMA-154 physical visual-servo gaze acceptance passed.\n'

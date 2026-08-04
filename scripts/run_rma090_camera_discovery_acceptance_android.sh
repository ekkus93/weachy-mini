#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA090_CAMERA_REPORT_DIR:-${ROOT_DIR}/build/rma090-camera-device-report}"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
PACKAGE_NAME="com.ekkus.weachymini"
RESULT_FILE_NAME="rma090-camera-discovery-state.json"
REMOTE_RESULT_PATH="/sdcard/Android/data/${PACKAGE_NAME}/files/${RESULT_FILE_NAME}"
TIMEOUT_SECONDS="${RMA090_CAMERA_TIMEOUT_SECONDS:-45}"
POLL_SECONDS="${RMA090_CAMERA_POLL_SECONDS:-0.5}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! -s "${FOREGROUND_HELPER}" ]]; then
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
fi
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'Camera discovery timeout must be a positive integer: %s\n' \
        "${TIMEOUT_SECONDS}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null

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
    "${ADB[@]}" shell dumpsys activity activities > "${REPORT_DIR}/activity.txt"
    "${ADB[@]}" shell dumpsys window windows > "${REPORT_DIR}/window.txt"
    "${ADB[@]}" shell dumpsys window policy > "${REPORT_DIR}/window-policy.txt"
    "${ADB[@]}" shell dumpsys power > "${REPORT_DIR}/power.txt"
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt"
    "${ADB[@]}" shell \
        "ls -laR '/sdcard/Android/data/${PACKAGE_NAME}' 2>&1" \
        > "${REPORT_DIR}/external-files.txt"
    "${ADB[@]}" shell \
        "run-as '${PACKAGE_NAME}' sh -c 'pwd; find . -maxdepth 4 -type f -print' 2>&1" \
        > "${REPORT_DIR}/internal-files.txt"
    "${ADB[@]}" exec-out run-as "${PACKAGE_NAME}" \
        cat "${REMOTE_RESULT_PATH}" \
        > "${REPORT_DIR}/camera-state-device.json" \
        2> "${REPORT_DIR}/camera-state-device-error.txt" || true
    "${ADB[@]}" shell \
        "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" \
        > "${REPORT_DIR}/camera-state-shell.json" \
        2> "${REPORT_DIR}/camera-state-shell-error.txt" || true
    "${ADB[@]}" exec-out screencap -p > "${REPORT_DIR}/device-screen.png"
}

cleanup()
{
    local exit_code=$?
    trap - EXIT
    if (( exit_code != 0 )); then
        capture_diagnostics
    fi
    set +e
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
    "${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10
    exit "${exit_code}"
}
trap cleanup EXIT

read_device_report()
{
    local report_json
    report_json="$(
        "${ADB[@]}" exec-out run-as "${PACKAGE_NAME}" \
            cat "${REMOTE_RESULT_PATH}" 2>/dev/null \
            | tr -d '\r' \
            || true
    )"
    if [[ -n "${report_json}" ]]; then
        printf '%s' "${report_json}"
        return
    fi

    report_json="$(
        "${ADB[@]}" shell \
            "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" \
            2>/dev/null \
            | tr -d '\r' \
            || true
    )"
    printf '%s' "${report_json}"
}

remove_device_report()
{
    "${ADB[@]}" exec-out run-as "${PACKAGE_NAME}" \
        rm -f "${REMOTE_RESULT_PATH}" >/dev/null 2>&1 || true
    "${ADB[@]}" shell rm -f "${REMOTE_RESULT_PATH}" >/dev/null 2>&1 || true
}

read_json_field()
{
    local report_json="$1"
    local field="$2"
    python3 - "${report_json}" "${field}" <<'PY'
from __future__ import annotations

import json
import sys

try:
    report = json.loads(sys.argv[1])
except json.JSONDecodeError:
    print("")
else:
    value = report.get(sys.argv[2], "")
    if isinstance(value, bool):
        print("true" if value else "false")
    else:
        print(value)
PY
}

wait_for_permission_state()
{
    local expected_permission="$1"
    local destination="$2"
    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    local report_json=""
    local observed_permission=""
    while true; do
        report_json="$(read_device_report)"
        if [[ -n "${report_json}" ]]; then
            printf '%s\n' "${report_json}" > "${REPORT_DIR}/camera-state-latest.json"
            observed_permission="$(read_json_field "${report_json}" permission)"
            if [[ "${observed_permission}" == "${expected_permission}" ]]; then
                printf '%s\n' "${report_json}" > "${destination}"
                return
            fi
            if [[ "${observed_permission}" == "Faulted" || \
                "${observed_permission}" == "Unsupported" ]]; then
                printf 'Camera discovery entered terminal state %s while waiting for %s: %s\n' \
                    "${observed_permission}" "${expected_permission}" "${report_json}" >&2
                return 1
            fi
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Timed out waiting for camera permission state %s; last state: %s\n' \
                "${expected_permission}" "${report_json:-none}" >&2
            return 1
        fi
        if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
            printf 'Unity application exited while waiting for camera state %s.\n' \
                "${expected_permission}" >&2
            return 1
        fi
        sleep "${POLL_SECONDS}"
    done
}

launch_application()
{
    local suffix="$1"
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
    remove_device_report
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
        > "${REPORT_DIR}/prepare-${suffix}.txt"
    "${ADB[@]}" shell am start -W \
        -n "${LAUNCH_COMPONENT}" \
        -a android.intent.action.MAIN \
        -c android.intent.category.LAUNCHER \
        > "${REPORT_DIR}/launch-${suffix}.txt"
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
        > "${REPORT_DIR}/focus-${suffix}.txt"
}

{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'manufacturer=%s\n' "$("${ADB[@]}" shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' "$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
} > "${REPORT_DIR}/device.txt"

"${ADB[@]}" install -r "${APK_PATH}" > "${REPORT_DIR}/install.txt"
"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/package-installed.txt"
if ! grep -q 'android.permission.CAMERA' "${REPORT_DIR}/package-installed.txt"; then
    printf '%s\n' 'Built APK does not declare android.permission.CAMERA.' >&2
    exit 1
fi

LAUNCH_COMPONENT="$(
    "${ADB[@]}" shell cmd package resolve-activity --brief \
        -a android.intent.action.MAIN \
        -c android.intent.category.LAUNCHER \
        "${PACKAGE_NAME}" \
        | tr -d '\r' \
        | tail -n 1
)"
if [[ -z "${LAUNCH_COMPONENT}" || "${LAUNCH_COMPONENT}" != */* ]]; then
    printf 'Could not resolve the installed launcher activity: %s\n' \
        "${LAUNCH_COMPONENT}" >&2
    exit 1
fi
printf '%s\n' "${LAUNCH_COMPONENT}" > "${REPORT_DIR}/launch-component.txt"

"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/clear.txt"
"${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA \
    > "${REPORT_DIR}/initial-revoke.txt" 2>&1 || true
"${ADB[@]}" logcat -c || true
launch_application no-permission
wait_for_permission_state \
    NotRequested \
    "${REPORT_DIR}/camera-state-not-requested.json"
"${ADB[@]}" logcat -d -v brief > "${REPORT_DIR}/logcat-not-requested.txt" || true
"${ADB[@]}" shell dumpsys window windows \
    > "${REPORT_DIR}/window-not-requested.txt"
if grep -Eiq \
    'GrantPermissionsActivity|permissioncontroller.*grant|PermissionActivity' \
    "${REPORT_DIR}/window-not-requested.txt"; then
    printf '%s\n' 'The app requested camera permission during startup.' >&2
    exit 1
fi
"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/package-not-requested.txt"
if grep -Eq \
    '^[[:space:]]*android\.permission\.CAMERA: granted=true' \
    "${REPORT_DIR}/package-not-requested.txt"; then
    printf '%s\n' 'Camera permission was unexpectedly granted before a user action.' >&2
    exit 1
fi

"${ADB[@]}" shell pm grant "${PACKAGE_NAME}" android.permission.CAMERA \
    > "${REPORT_DIR}/grant.txt"
"${ADB[@]}" logcat -c || true
launch_application granted
wait_for_permission_state \
    Granted \
    "${REPORT_DIR}/camera-state-granted.json"
"${ADB[@]}" logcat -d -v brief > "${REPORT_DIR}/logcat-granted.txt" || true

"${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA \
    > "${REPORT_DIR}/revoke.txt"
"${ADB[@]}" logcat -c || true
launch_application revoked
wait_for_permission_state \
    Revoked \
    "${REPORT_DIR}/camera-state-revoked.json"
"${ADB[@]}" logcat -d -v brief > "${REPORT_DIR}/logcat-revoked.txt" || true

capture_diagnostics
python3 - "${REPORT_DIR}" <<'PY'
from __future__ import annotations

import json
import sys
from pathlib import Path

report_dir = Path(sys.argv[1])
not_requested = json.loads(
    (report_dir / "camera-state-not-requested.json").read_text(encoding="utf-8")
)
granted = json.loads(
    (report_dir / "camera-state-granted.json").read_text(encoding="utf-8")
)
revoked = json.loads(
    (report_dir / "camera-state-revoked.json").read_text(encoding="utf-8")
)

if not_requested.get("status") != "ok" or not_requested.get("permission") != "NotRequested":
    raise SystemExit(f"Invalid not-requested camera state: {not_requested}")
if not_requested.get("camera_count") != 0:
    raise SystemExit(f"Camera inventory was exposed without permission: {not_requested}")

if granted.get("status") != "ok" or granted.get("permission") != "Granted":
    raise SystemExit(f"Invalid granted camera state: {granted}")
camera_count = int(granted.get("camera_count", 0))
front_count = int(granted.get("front_count", 0))
rear_count = int(granted.get("rear_count", 0))
available_count = int(granted.get("available_count", 0))
calibrated_count = int(granted.get("calibrated_count", 0))
cameras = granted.get("cameras") or []
if camera_count <= 0 or len(cameras) != camera_count:
    raise SystemExit(f"Granted camera inventory is empty or inconsistent: {granted}")
if front_count <= 0 or rear_count <= 0:
    raise SystemExit(f"Front and rear camera discovery is incomplete: {granted}")
if available_count <= 0:
    raise SystemExit(f"No discovered camera is available: {granted}")
if granted.get("selection_available") is not True:
    raise SystemExit(f"Camera selection was not enabled after discovery: {granted}")

valid_intrinsics = {"AndroidCalibration", "CalibrationFallbackRequired"}
valid_facing = {"Front", "Rear", "External", "Unknown"}
valid_availability = {
    "Available",
    "InUseOrUnavailable",
    "Disabled",
    "Disconnected",
    "Unknown",
}
for camera in cameras:
    if not camera.get("id"):
        raise SystemExit(f"Camera identifier is missing: {camera}")
    if camera.get("facing") not in valid_facing:
        raise SystemExit(f"Invalid camera facing: {camera}")
    if camera.get("availability") not in valid_availability:
        raise SystemExit(f"Invalid camera availability: {camera}")
    if camera.get("sensor_orientation_degrees") not in {0, 90, 180, 270}:
        raise SystemExit(f"Invalid sensor orientation: {camera}")
    resolutions = camera.get("analysis_resolutions") or []
    if int(camera.get("analysis_resolution_count", 0)) <= 0 or not resolutions:
        raise SystemExit(f"Camera has no YUV analysis resolution: {camera}")
    if camera.get("largest_analysis_resolution") in {None, "", "none"}:
        raise SystemExit(f"Largest analysis resolution is missing: {camera}")
    for resolution in resolutions:
        if int(resolution.get("width", 0)) <= 0 or int(resolution.get("height", 0)) <= 0:
            raise SystemExit(f"Invalid analysis resolution: {camera}")
    if camera.get("intrinsics_source") not in valid_intrinsics:
        raise SystemExit(f"Invalid intrinsics provenance: {camera}")
    if not camera.get("calibration_fallback"):
        raise SystemExit(f"Calibration fallback is missing: {camera}")

if revoked.get("status") != "ok" or revoked.get("permission") != "Revoked":
    raise SystemExit(f"Invalid revoked camera state: {revoked}")
if revoked.get("camera_count") != 0:
    raise SystemExit(f"Camera inventory survived permission revocation: {revoked}")

report = {
    "status": "ok",
    "evidence_transport": "application_persistent_json",
    "permission_requested_on_startup": False,
    "not_requested_observed": True,
    "granted_observed": True,
    "revoked_observed": True,
    "camera_count": camera_count,
    "front_count": front_count,
    "rear_count": rear_count,
    "available_count": available_count,
    "intrinsics_count": calibrated_count,
    "camera_details": cameras,
}
(report_dir / "rma090-camera-discovery.json").write_text(
    json.dumps(report, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
print(json.dumps(report, indent=2, sort_keys=True))
PY

if [[ ! -s "${REPORT_DIR}/device-screen.png" ]]; then
    printf '%s\n' 'RMA-090 physical-device screenshot is empty.' >&2
    exit 1
fi

printf 'RMA-090 Android camera discovery acceptance passed on %s.\n' \
    "${DEVICE_SERIAL}"

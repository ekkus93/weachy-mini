#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA090_CAMERA_REPORT_DIR:-${ROOT_DIR}/build/rma090-camera-device-report}"
PACKAGE_NAME="com.ekkus.weachymini"
TIMEOUT_SECONDS="${RMA090_CAMERA_TIMEOUT_SECONDS:-45}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
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
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt"
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
    exit "${exit_code}"
}
trap cleanup EXIT

wait_for_log()
{
    local pattern="$1"
    local destination="$2"
    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    while true; do
        "${ADB[@]}" logcat -d -v brief > "${destination}"
        if grep -E --quiet -- "${pattern}" "${destination}"; then
            return
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Timed out waiting for camera log pattern: %s\n' "${pattern}" >&2
            return 1
        fi
        if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
            printf 'Unity application exited while waiting for: %s\n' "${pattern}" >&2
            return 1
        fi
        sleep 1
    done
}

launch_application()
{
    local suffix="$1"
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
    "${ADB[@]}" shell am start -W \
        -n "${LAUNCH_COMPONENT}" \
        -a android.intent.action.MAIN \
        -c android.intent.category.LAUNCHER \
        > "${REPORT_DIR}/launch-${suffix}.txt"
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
"${ADB[@]}" logcat -c
launch_application no-permission
wait_for_log \
    'RMA090_CAMERA_CAPABILITIES permission=NotRequested;' \
    "${REPORT_DIR}/logcat-not-requested.txt"
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
if grep -A4 'android.permission.CAMERA' "${REPORT_DIR}/package-not-requested.txt" \
    | grep -q 'granted=true'; then
    printf '%s\n' 'Camera permission was unexpectedly granted before a user action.' >&2
    exit 1
fi

"${ADB[@]}" shell pm grant "${PACKAGE_NAME}" android.permission.CAMERA \
    > "${REPORT_DIR}/grant.txt"
"${ADB[@]}" logcat -c
launch_application granted
wait_for_log \
    'RMA090_CAMERA_CAPABILITIES permission=Granted; cameras=[1-9][0-9]*; front=[1-9][0-9]*; rear=[1-9][0-9]*;' \
    "${REPORT_DIR}/logcat-granted.txt"
wait_for_log \
    'RMA090_CAMERA id=.*orientation=(0|90|180|270).*resolutions=[1-9][0-9]*.*intrinsics=(AndroidCalibration|CalibrationFallbackRequired)' \
    "${REPORT_DIR}/logcat-camera-details.txt"

"${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA \
    > "${REPORT_DIR}/revoke.txt"
"${ADB[@]}" logcat -c
launch_application revoked
wait_for_log \
    'RMA090_CAMERA_CAPABILITIES permission=Revoked;' \
    "${REPORT_DIR}/logcat-revoked.txt"

capture_diagnostics
python3 - "${REPORT_DIR}" <<'PY'
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

report_dir = Path(sys.argv[1])
not_requested = (report_dir / "logcat-not-requested.txt").read_text(
    encoding="utf-8", errors="replace"
)
granted = (report_dir / "logcat-camera-details.txt").read_text(
    encoding="utf-8", errors="replace"
)
revoked = (report_dir / "logcat-revoked.txt").read_text(
    encoding="utf-8", errors="replace"
)
summary_match = re.search(
    r"RMA090_CAMERA_CAPABILITIES permission=Granted; cameras=(\d+); "
    r"front=(\d+); rear=(\d+); available=(\d+); intrinsics=(\d+)/(\d+);",
    granted,
)
if summary_match is None:
    raise SystemExit("Granted camera summary is missing from device logs.")
detail_matches = re.findall(
    r"RMA090_CAMERA id=([^ ]+) facing=([^ ]+) orientation=(\d+) "
    r"availability=([^ ]+) resolutions=(\d+) top=([^ ]+) intrinsics=([^ ]+)",
    granted,
)
if not detail_matches:
    raise SystemExit("Per-camera discovery details are missing from device logs.")
report = {
    "status": "ok",
    "permission_requested_on_startup": False,
    "not_requested_observed": "permission=NotRequested" in not_requested,
    "granted_observed": True,
    "revoked_observed": "permission=Revoked" in revoked,
    "camera_count": int(summary_match.group(1)),
    "front_count": int(summary_match.group(2)),
    "rear_count": int(summary_match.group(3)),
    "available_count": int(summary_match.group(4)),
    "intrinsics_count": int(summary_match.group(5)),
    "camera_details": [
        {
            "id": values[0],
            "facing": values[1],
            "orientation_degrees": int(values[2]),
            "availability": values[3],
            "analysis_resolution_count": int(values[4]),
            "largest_analysis_resolution": values[5],
            "intrinsics_source": values[6],
        }
        for values in detail_matches
    ],
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

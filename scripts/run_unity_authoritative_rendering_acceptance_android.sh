#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${UNITY_AUTHORITATIVE_REPORT_DIR:-${ROOT_DIR}/build/unity-authoritative-device-report}"
PACKAGE_NAME="com.ekkus.weachymini"
SUCCESS_MARKER="WEACHY_AUTHORITATIVE_ACCEPTANCE "
FAILURE_MARKER="WEACHY_AUTHORITATIVE_ACCEPTANCE_FAILURE "
TIMEOUT_SECONDS="${UNITY_AUTHORITATIVE_TIMEOUT_SECONDS:-90}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null

select_device_serial()
{
    mapfile -t physical_serials < <(
        "${ADB_BIN}" devices \
            | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )
    local -a arm64_serials=()
    local serial
    for serial in "${physical_serials[@]}"; do
        local abi
        local sdk
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
        sdk="$("${ADB_BIN}" -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\r')"
        printf 'physical_device=%s abi=%s sdk=%s\n' "${serial}" "${abi}" "${sdk}"
        if [[ "${abi}" == "arm64-v8a" && "${sdk}" =~ ^[0-9]+$ ]] && (( sdk >= 26 )); then
            arm64_serials+=("${serial}")
        fi
    done
    if (( ${#arm64_serials[@]} != 1 )); then
        printf 'Expected one physical arm64-v8a API-26+ device; found %s.\n' \
            "${#arm64_serials[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi
    printf '%s\n' "${arm64_serials[0]}"
}

DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:-$(select_device_serial | tail -n 1)}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")
rm -rf -- "${REPORT_DIR}"
mkdir -p "${REPORT_DIR}"

{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'manufacturer=%s\n' "$("${ADB[@]}" shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' "$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
    android_release="$("${ADB[@]}" shell getprop ro.build.version.release | tr -d '\r')"
    printf 'android_release=%s\n' "${android_release}"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
} > "${REPORT_DIR}/device.txt"

"${ADB[@]}" install -r "${APK_PATH}" > "${REPORT_DIR}/install.txt"
"${ADB[@]}" shell pm path "${PACKAGE_NAME}" > "${REPORT_DIR}/package-path.txt"
"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
"${ADB[@]}" logcat -c
"${ADB[@]}" shell monkey \
    -p "${PACKAGE_NAME}" \
    -c android.intent.category.LAUNCHER \
    1 > "${REPORT_DIR}/launch.txt"

start_epoch="$(date +%s)"
report_line=""
while true; do
    "${ADB[@]}" logcat -d -v raw > "${REPORT_DIR}/logcat.txt"
    if grep -F "${FAILURE_MARKER}" "${REPORT_DIR}/logcat.txt" >/dev/null; then
        grep -F "${FAILURE_MARKER}" "${REPORT_DIR}/logcat.txt" | tail -n 1 >&2
        exit 1
    fi
    report_line="$(grep -F "${SUCCESS_MARKER}" "${REPORT_DIR}/logcat.txt" | tail -n 1 || true)"
    if [[ -n "${report_line}" ]]; then
        break
    fi
    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf '%s\n' 'Unity application exited before authoritative acceptance completed.' >&2
        exit 1
    fi
    now_epoch="$(date +%s)"
    if (( now_epoch - start_epoch >= TIMEOUT_SECONDS )); then
        printf 'Timed out after %s seconds waiting for authoritative acceptance.\n' \
            "${TIMEOUT_SECONDS}" >&2
        exit 1
    fi
    sleep 2
done

report_json="${report_line#*${SUCCESS_MARKER}}"
printf '%s\n' "${report_json}" > "${REPORT_DIR}/authoritative-rendering.json"
python3 - "${REPORT_DIR}/authoritative-rendering.json" <<'PY'
from __future__ import annotations

import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
report = json.loads(path.read_text(encoding="utf-8"))
expected_true = (
    "body_yaw_moved",
    "head_moved",
    "right_antenna_moved",
    "left_antenna_moved",
    "renderer_structure_valid",
)
if report.get("status") != "ok":
    raise SystemExit(f"acceptance status is not ok: {report}")
if report.get("body_count") != 18:
    raise SystemExit(f"unexpected body count: {report}")
if not str(report.get("model_hash", "")).isdigit() or int(report["model_hash"]) == 0:
    raise SystemExit(f"invalid model hash: {report}")
if report.get("moved_body_count", 0) < 10:
    raise SystemExit(f"insufficient rendered body motion: {report}")
if report.get("moved_stewart_link_count") != 6:
    raise SystemExit(f"not all Stewart links moved: {report}")
for key in expected_true:
    if report.get(key) is not True:
        raise SystemExit(f"required acceptance flag {key} is false: {report}")
if report.get("hidden_kinematic_fallback") is not False:
    raise SystemExit(f"hidden fallback was reported: {report}")
if report.get("renderer_status") != "Rendering":
    raise SystemExit(f"renderer did not remain authoritative: {report}")
if report.get("runtime_status") != "Running":
    raise SystemExit(f"runtime did not remain running: {report}")
if report.get("reset_continuity_id") == report.get("initial_continuity_id"):
    raise SystemExit(f"reset did not advance continuity: {report}")
sequences = [
    int(report["initial_sequence"]),
    int(report["pose_a_sequence"]),
    int(report["pose_b_sequence"]),
]
if not sequences[0] < sequences[1] < sequences[2]:
    raise SystemExit(f"motion sequences are not ordered: {report}")
print(json.dumps(report, indent=2, sort_keys=True))
PY

"${ADB[@]}" exec-out screencap -p > "${REPORT_DIR}/authoritative-rendering.png"
if [[ ! -s "${REPORT_DIR}/authoritative-rendering.png" ]]; then
    printf '%s\n' 'Physical-device screenshot is empty.' >&2
    exit 1
fi
"${ADB[@]}" shell dumpsys activity activities \
    > "${REPORT_DIR}/activity.txt"
"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/package.txt"
printf 'Authoritative Unity rendering acceptance passed on %s.\n' "${DEVICE_SERIAL}"

#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

APK_PATH="${1:-artifacts/android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${2:-artifacts/rma180-performance-android}"
PROFILE_SECONDS="${RMA180_PROFILE_SECONDS:-300}"
DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:?Set REACHY_ANDROID_SERIAL to the dedicated physical Android device serial.}"
ADB_BIN="${ADB_BIN:-adb}"
PACKAGE_NAME="com.ekkus.weachymini"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT_PATH="${REMOTE_FILES_DIR}/rma180-performance-acceptance.json"
TIMEOUT_SECONDS=$((PROFILE_SECONDS * 2 + 180))
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")

[[ "${PROFILE_SECONDS}" =~ ^[0-9]+$ ]] || {
  printf '%s\n' 'RMA180_PROFILE_SECONDS must be an integer.' >&2
  exit 2
}
(( PROFILE_SECONDS >= 10 && PROFILE_SECONDS <= 3600 )) || {
  printf '%s\n' 'RMA180_PROFILE_SECONDS must be between 10 and 3600.' >&2
  exit 2
}
[[ -f "${APK_PATH}" ]] || {
  printf 'RMA-180 APK is missing: %s\n' "${APK_PATH}" >&2
  exit 1
}
command -v "${ADB_BIN}" >/dev/null 2>&1 || {
  printf 'adb unavailable: %s\n' "${ADB_BIN}" >&2
  exit 1
}
command -v python3 >/dev/null 2>&1 || {
  printf '%s\n' 'python3 is required.' >&2
  exit 1
}

mkdir -p "${REPORT_DIR}"
"${ADB[@]}" get-state >/dev/null
"${ADB[@]}" install -r "${APK_PATH}" > "${REPORT_DIR}/install.txt"

launch_component="$(
  "${ADB[@]}" shell cmd package resolve-activity --brief \
    -a android.intent.action.MAIN \
    -c android.intent.category.LAUNCHER \
    "${PACKAGE_NAME}" | tr -d '\r' | tail -n 1
)"
[[ "${launch_component}" == */* ]] || {
  printf 'Could not resolve launcher component: %s\n' "${launch_component}" >&2
  exit 1
}

capture_state() {
  local prefix="$1"
  timeout 15s "${ADB[@]}" shell dumpsys battery \
    > "${REPORT_DIR}/${prefix}-battery.txt" 2>&1 || true
  timeout 15s "${ADB[@]}" shell dumpsys meminfo "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/${prefix}-meminfo.txt" 2>&1 || true
  timeout 15s "${ADB[@]}" shell dumpsys thermalservice \
    > "${REPORT_DIR}/${prefix}-thermalservice.txt" 2>&1 || true
}

capture_state before
"${ADB[@]}" shell rm -f "${REMOTE_RESULT_PATH}" >/dev/null 2>&1 || true
"${ADB[@]}" logcat -c >/dev/null 2>&1 || true
"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
"${ADB[@]}" shell am start -W \
  -n "${launch_component}" \
  -a android.intent.action.MAIN \
  -c android.intent.category.LAUNCHER \
  --ez reachy_rma180_performance_acceptance true \
  --ei reachy_rma180_profile_seconds "${PROFILE_SECONDS}" \
  > "${REPORT_DIR}/launch.txt"

start_epoch="$(date +%s)"
while true; do
  report="$(
    "${ADB[@]}" shell \
      "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" \
      2>/dev/null | tr -d '\r' || true
  )"
  if [[ -n "${report}" ]]; then
    printf '%s\n' "${report}" > "${REPORT_DIR}/rma180-performance-acceptance.json"
    break
  fi

  "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null || {
    printf '%s\n' 'Unity exited before RMA-180 produced a report.' >&2
    exit 1
  }
  if (( $(date +%s) - start_epoch >= TIMEOUT_SECONDS )); then
    printf 'RMA-180 timed out after %s seconds.\n' "${TIMEOUT_SECONDS}" >&2
    exit 1
  fi
  sleep 2
done

capture_state after
timeout 20s "${ADB[@]}" logcat -d -v threadtime \
  > "${REPORT_DIR}/logcat.txt" 2>&1 || true

python3 - "${REPORT_DIR}/rma180-performance-acceptance.json" <<'PY'
import json
import sys
from pathlib import Path

report = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
assert report.get("status") == "passed", report
profiles = report.get("profiles")
assert isinstance(profiles, list) and len(profiles) == 2, profiles
assert [item.get("target_fps") for item in profiles] == [30, 60], profiles
for profile in profiles:
    timings = {item["workload"]: item for item in profile["timings"]}
    assert timings["NativePhysics"]["sample_count"] > 0, profile
    assert timings["UnityRendering"]["sample_count"] > 0, profile
    assert profile["resources"]["sample_count"] > 0, profile
    for workload in (
        "CameraAcquisition",
        "CameraWarp",
        "LightweightTracking",
        "LocalLlm",
        "Audio",
        "Network",
    ):
        item = timings[workload]
        if item["sample_count"] == 0:
            assert item["availability"] == "Unavailable"
            assert item["availability_reason"]
print("RMA-180 performance acceptance report validated.")
PY

printf 'RMA-180 performance evidence: %s\n' "${REPORT_DIR}"

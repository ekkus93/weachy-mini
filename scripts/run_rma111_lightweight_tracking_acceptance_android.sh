#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA111_TRACKING_REPORT_DIR:-${ROOT_DIR}/build/rma111-lightweight-tracking-report}"
PACKAGE_NAME="com.ekkus.weachymini"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
RESULT_FILE="rma111-lightweight-tracking-state.json"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT="${REMOTE_FILES_DIR}/${RESULT_FILE}"
TIMEOUT_SECONDS="${RMA111_TRACKING_TIMEOUT_SECONDS:-120}"
POLL_SECONDS="${RMA111_TRACKING_POLL_SECONDS:-0.5}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'RMA-111 timeout must be a positive integer: %s\n' \
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
    --ez reachy_rma111_acceptance true \
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
        printf 'Timed out waiting for RMA-111 tracking evidence.\n' >&2
        exit 1
    fi
    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf 'Unity application exited before RMA-111 evidence was written.\n' >&2
        exit 1
    fi
    sleep "${POLL_SECONDS}"
done

printf '%s\n' "${report_json}" > "${REPORT_DIR}/${RESULT_FILE}"
python3 - "${REPORT_DIR}/${RESULT_FILE}" <<'PY'
from __future__ import annotations

import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
report = json.loads(path.read_text(encoding="utf-8"))
required_true = (
    "acceptance_enabled",
    "stable_face_id",
    "stable_person_id",
    "invalid_center_suppressed",
)
if report.get("status") != "passed":
    raise SystemExit(f"RMA-111 acceptance did not pass: {report}")
if any(report.get(field) is not True for field in required_true):
    raise SystemExit(f"RMA-111 required truth fields failed: {report}")
if int(report.get("first_face_count", 0)) < 1:
    raise SystemExit(f"RMA-111 detected no face: {report}")
if int(report.get("first_person_count", 0)) < 1:
    raise SystemExit(f"RMA-111 detected no person on the first frame: {report}")
if int(report.get("second_person_count", 0)) < 1:
    raise SystemExit(f"RMA-111 detected no person on the second frame: {report}")
if report.get("first_face_id") != report.get("second_face_id"):
    raise SystemExit(f"RMA-111 stable face ID mismatch: {report}")
if not str(report.get("first_person_id", "")):
    raise SystemExit(f"RMA-111 first person ID is empty: {report}")
if report.get("first_person_id") != report.get("second_person_id"):
    raise SystemExit(f"RMA-111 stable person ID mismatch: {report}")
if not str(report.get("backend_id", "")).startswith("google-mlkit-bundled"):
    raise SystemExit(f"RMA-111 backend identity is not bundled ML Kit: {report}")
if report.get("network_model_download_used") is not False:
    raise SystemExit(f"RMA-111 unexpectedly used a model download: {report}")
if int(report.get("vlm_invocation_count", -1)) != 0:
    raise SystemExit(f"RMA-111 invoked a VLM: {report}")
if report.get("object_tracking_enabled") is not False:
    raise SystemExit(f"RMA-111 object tracking was enabled without evidence: {report}")
if report.get("motion_tracking_enabled") is not False:
    raise SystemExit(f"RMA-111 motion tracking was enabled without evidence: {report}")
sha = str(report.get("fixture_sha256", ""))
if len(sha) != 64 or any(ch not in "0123456789abcdef" for ch in sha):
    raise SystemExit(f"RMA-111 fixture SHA is invalid: {report}")
PY

capture_diagnostics
sha256sum "${APK_PATH}" > "${REPORT_DIR}/apk.sha256"
sha256sum "${REPORT_DIR}/${RESULT_FILE}" > "${REPORT_DIR}/report.sha256"
printf 'device_serial=%s\n' "${DEVICE_SERIAL}" > "${REPORT_DIR}/environment.txt"
printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')" \
    >> "${REPORT_DIR}/environment.txt"
printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')" \
    >> "${REPORT_DIR}/environment.txt"
printf 'RMA-111 bundled on-device face/person tracking acceptance passed.\n'

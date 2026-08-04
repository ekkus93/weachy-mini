#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA092_CAMERA_REPORT_DIR:-${ROOT_DIR}/build/rma092-camera-texture-report}"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
PACKAGE_NAME="com.ekkus.weachymini"
ACQUISITION_RESULT_FILE="rma091-camera-acquisition-state.json"
ACQUISITION_COMMAND_FILE="rma091-camera-acquisition-command.json"
TEXTURE_RESULT_FILE="rma092-camera-texture-state.json"
REAR_TEXTURE_FILE="rma092-rear.png"
ROTATED_TEXTURE_FILE="rma092-rear-rotated.png"
FRONT_TEXTURE_FILE="rma092-front.png"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_ACQUISITION_RESULT="${REMOTE_FILES_DIR}/${ACQUISITION_RESULT_FILE}"
REMOTE_ACQUISITION_COMMAND="${REMOTE_FILES_DIR}/${ACQUISITION_COMMAND_FILE}"
REMOTE_TEXTURE_RESULT="${REMOTE_FILES_DIR}/${TEXTURE_RESULT_FILE}"
TIMEOUT_SECONDS="${RMA092_CAMERA_TIMEOUT_SECONDS:-75}"
POLL_SECONDS="${RMA092_CAMERA_POLL_SECONDS:-0.5}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! -s "${FOREGROUND_HELPER}" ]]; then
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
fi
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'Camera texture timeout must be a positive integer: %s\n' \
        "${TIMEOUT_SECONDS}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null
command -v python3 >/dev/null
command -v sha256sum >/dev/null

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

ORIGINAL_ACCELEROMETER_ROTATION="$(
    "${ADB[@]}" shell settings get system accelerometer_rotation \
        | tr -d '\r' \
        || true
)"
ORIGINAL_USER_ROTATION="$(
    "${ADB[@]}" shell settings get system user_rotation \
        | tr -d '\r' \
        || true
)"

read_remote_file()
{
    local path="$1"
    "${ADB[@]}" shell \
        "if test -f '${path}'; then cat '${path}'; fi" \
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
trap cleanup EXIT

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

write_command()
{
    local command_id="$1"
    local action="$2"
    local facing="${3:-}"
    local local_path="${REPORT_DIR}/command-${command_id}.json"
    python3 - "${command_id}" "${action}" "${facing}" > "${local_path}" <<'PY'
from __future__ import annotations

import json
import sys

print(json.dumps({
    "id": sys.argv[1],
    "action": sys.argv[2],
    "facing": sys.argv[3],
}, separators=(",", ":")))
PY
    "${ADB[@]}" shell mkdir -p "${REMOTE_FILES_DIR}"
    "${ADB[@]}" push "${local_path}" "${REMOTE_ACQUISITION_COMMAND}.tmp" \
        > "${REPORT_DIR}/push-${command_id}.txt"
    "${ADB[@]}" shell mv \
        "${REMOTE_ACQUISITION_COMMAND}.tmp" \
        "${REMOTE_ACQUISITION_COMMAND}"
}

json_field()
{
    local report_json="$1"
    local path="$2"
    python3 - "${report_json}" "${path}" <<'PY'
from __future__ import annotations

import json
import sys

try:
    value = json.loads(sys.argv[1])
    for component in sys.argv[2].split("."):
        if not isinstance(value, dict):
            value = ""
            break
        value = value.get(component, "")
except (json.JSONDecodeError, TypeError):
    value = ""

if isinstance(value, bool):
    print("true" if value else "false")
elif value is None:
    print("")
else:
    print(value)
PY
}

report_matches()
{
    local report_json="$1"
    local condition="$2"
    shift 2
    python3 - "${report_json}" "${condition}" "$@" <<'PY'
from __future__ import annotations

import json
import sys

try:
    report = json.loads(sys.argv[1])
except json.JSONDecodeError:
    raise SystemExit(1)

condition = sys.argv[2]
args = sys.argv[3:]

def integer(path: str) -> int:
    value = report
    for component in path.split("."):
        if not isinstance(value, dict):
            return 0
        value = value.get(component, 0)
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0

matched = False
if condition == "acquisition_ready":
    matched = (
        report.get("status") == "ok"
        and report.get("acceptance_enabled") is True
        and report.get("permission") == "Granted"
        and report.get("current_state") in {
            "Stopped", "PermissionRevoked", "Unavailable"
        }
    )
elif condition == "acquisition_running":
    command_id, facing = args
    matched = (
        report.get("status") == "ok"
        and report.get("last_command_id") == command_id
        and report.get("last_command_status") == "ok"
        and report.get("current_state") == "Running"
        and report.get("requested_facing") == facing
        and integer("accepted_frame_count") >= 5
        and report.get("metadata_monotonic") is True
        and report.get("all_frames_positive_timestamp") is True
    )
elif condition == "acquisition_stopped":
    command_id = args[0]
    matched = (
        report.get("last_command_id") == command_id
        and report.get("last_command_status") == "ok"
        and report.get("current_state") == "Stopped"
    )
elif condition == "texture_capture":
    capture_field = args[0]
    matched = (
        report.get("status") == "ok"
        and report.get("acceptance_enabled") is True
        and report.get("bridge_state") == "Ready"
        and report.get(capture_field) is True
        and integer("observed_frame_count") >= 3
        and integer("metadata_match_count") >= 1
        and integer("stale_frame_count") == 0
        and report.get("descriptor_monotonic") is True
        and report.get("timestamp_correspondence") is True
        and report.get("output_dimensions_valid") is True
        and report.get("mirror_contract_valid") is True
        and report.get("color_contract_valid") is True
        and report.get("captures_non_uniform") is True
        and report.get("captures_opaque") is True
    )

raise SystemExit(0 if matched else 1)
PY
}

wait_for_report()
{
    local remote_path="$1"
    local description="$2"
    local destination="$3"
    local condition="$4"
    shift 4
    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    local report_json=""
    while true; do
        report_json="$(read_remote_file "${remote_path}")"
        if [[ -n "${report_json}" ]]; then
            if report_matches "${report_json}" "${condition}" "$@"; then
                printf '%s\n' "${report_json}" > "${destination}"
                return
            fi
            status="$(json_field "${report_json}" status)"
            if [[ "${status}" == "error" ]]; then
                printf 'Evidence faulted while waiting for %s: %s\n' \
                    "${description}" "${report_json}" >&2
                return 1
            fi
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Timed out waiting for %s; last report: %s\n' \
                "${description}" "${report_json:-none}" >&2
            return 1
        fi
        if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
            printf 'Unity application exited while waiting for %s.\n' \
                "${description}" >&2
            return 1
        fi
        sleep "${POLL_SECONDS}"
    done
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

{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'manufacturer=%s\n' "$("${ADB[@]}" shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' "$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
    printf 'original_accelerometer_rotation=%s\n' "${ORIGINAL_ACCELEROMETER_ROTATION}"
    printf 'original_user_rotation=%s\n' "${ORIGINAL_USER_ROTATION}"
} > "${REPORT_DIR}/device.txt"

"${ADB[@]}" install -r "${APK_PATH}" > "${REPORT_DIR}/install.txt"
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
"${ADB[@]}" shell pm grant "${PACKAGE_NAME}" android.permission.CAMERA \
    > "${REPORT_DIR}/grant.txt"
remove_remote_evidence
"${ADB[@]}" logcat -c || true
launch_application initial
wait_for_report \
    "${REMOTE_ACQUISITION_RESULT}" \
    'the RMA-091 command service and granted camera permission' \
    "${REPORT_DIR}/00-ready.json" \
    acquisition_ready

write_command rear-1 start rear
wait_for_report \
    "${REMOTE_ACQUISITION_RESULT}" \
    'the rear CameraX session' \
    "${REPORT_DIR}/01-rear-running.json" \
    acquisition_running rear-1 Rear
wait_for_report \
    "${REMOTE_TEXTURE_RESULT}" \
    'the rear RGB texture capture' \
    "${REPORT_DIR}/02-rear-texture.json" \
    texture_capture rear_capture_written
"${ADB[@]}" exec-out screencap -p \
    > "${REPORT_DIR}/rear-screen.png"
test -s "${REPORT_DIR}/rear-screen.png"

write_command rotation-stop stop
wait_for_report \
    "${REMOTE_ACQUISITION_RESULT}" \
    'CameraX stop before display rotation' \
    "${REPORT_DIR}/03-rotation-stopped.json" \
    acquisition_stopped rotation-stop
"${ADB[@]}" shell settings put system accelerometer_rotation 0
CURRENT_USER_ROTATION="$(
    "${ADB[@]}" shell settings get system user_rotation | tr -d '\r'
)"
if [[ "${CURRENT_USER_ROTATION}" == "1" || \
      "${CURRENT_USER_ROTATION}" == "3" ]]; then
    ROTATED_USER_ROTATION=0
else
    ROTATED_USER_ROTATION=1
fi
printf '%s\n' "${ROTATED_USER_ROTATION}" \
    > "${REPORT_DIR}/rotated-user-rotation.txt"
"${ADB[@]}" shell settings put system user_rotation \
    "${ROTATED_USER_ROTATION}"
sleep 3
"${ADB[@]}" shell dumpsys display \
    > "${REPORT_DIR}/display-rotated.txt"
write_command rear-rotated start rear
wait_for_report \
    "${REMOTE_ACQUISITION_RESULT}" \
    'the rotated rear CameraX session' \
    "${REPORT_DIR}/04-rear-rotated-running.json" \
    acquisition_running rear-rotated Rear
wait_for_report \
    "${REMOTE_TEXTURE_RESULT}" \
    'the rotated rear RGB texture capture' \
    "${REPORT_DIR}/05-rear-rotated-texture.json" \
    texture_capture rotated_capture_written
"${ADB[@]}" exec-out screencap -p \
    > "${REPORT_DIR}/rear-rotated-screen.png"
test -s "${REPORT_DIR}/rear-rotated-screen.png"

write_command switch-stop stop
wait_for_report \
    "${REMOTE_ACQUISITION_RESULT}" \
    'CameraX stop before front-camera switch' \
    "${REPORT_DIR}/06-switch-stopped.json" \
    acquisition_stopped switch-stop
write_command front-1 start front
wait_for_report \
    "${REMOTE_ACQUISITION_RESULT}" \
    'the front CameraX session' \
    "${REPORT_DIR}/07-front-running.json" \
    acquisition_running front-1 Front
wait_for_report \
    "${REMOTE_TEXTURE_RESULT}" \
    'the mirrored front RGB texture capture' \
    "${REPORT_DIR}/08-front-texture.json" \
    texture_capture front_capture_written
"${ADB[@]}" exec-out screencap -p \
    > "${REPORT_DIR}/front-screen.png"
test -s "${REPORT_DIR}/front-screen.png"

pull_remote_evidence "${TEXTURE_RESULT_FILE}" "texture-final.json"
pull_remote_evidence "${REAR_TEXTURE_FILE}" "rear-texture.png"
pull_remote_evidence "${ROTATED_TEXTURE_FILE}" "rear-rotated-texture.png"
pull_remote_evidence "${FRONT_TEXTURE_FILE}" "front-texture.png"
capture_diagnostics

python3 - "${REPORT_DIR}" <<'PY'
from __future__ import annotations

import hashlib
import json
import struct
import sys
from pathlib import Path

report_dir = Path(sys.argv[1])
report = json.loads((report_dir / "texture-final.json").read_text(encoding="utf-8"))

required_true = (
    "descriptor_monotonic",
    "timestamp_correspondence",
    "output_dimensions_valid",
    "mirror_contract_valid",
    "color_contract_valid",
    "captures_non_uniform",
    "captures_opaque",
    "rear_capture_written",
    "rotated_capture_written",
    "front_capture_written",
)
if report.get("status") != "ok" or report.get("acceptance_enabled") is not True:
    raise SystemExit(f"RMA-092 texture evidence is unhealthy: {report}")
for field in required_true:
    if report.get(field) is not True:
        raise SystemExit(f"RMA-092 texture evidence failed {field}: {report}")
if int(report.get("observed_frame_count", 0)) < 9:
    raise SystemExit(f"Too few RGB texture frames were observed: {report}")
if int(report.get("metadata_match_count", 0)) < 1:
    raise SystemExit(f"No exact texture/metadata timestamp match was observed: {report}")
if int(report.get("capture_count", 0)) < 3:
    raise SystemExit(f"All three RGB texture captures were not written: {report}")
if int(report.get("stale_frame_count", 0)) != 0:
    raise SystemExit(f"The Unity texture bridge accepted a stale frame: {report}")
if report.get("bridge_state") != "Ready":
    raise SystemExit(f"The final texture bridge state is not Ready: {report}")
if report.get("rear_rotation_degrees") == report.get("rotated_rotation_degrees"):
    raise SystemExit(f"Display rotation did not change RGB texture orientation: {report}")
frame = report.get("frame") or {}
if int(frame.get("output_width", 0)) <= 0 or int(frame.get("output_height", 0)) <= 0:
    raise SystemExit(f"The final RGB texture has invalid output dimensions: {report}")
if frame.get("lens_facing") != "Front" or frame.get("mirrored") is not True:
    raise SystemExit(f"The final front texture is not marked mirrored: {report}")

png_names = (
    "rear-texture.png",
    "rear-rotated-texture.png",
    "front-texture.png",
    "rear-screen.png",
    "rear-rotated-screen.png",
    "front-screen.png",
)
artifacts = {}
for name in png_names:
    path = report_dir / name
    data = path.read_bytes()
    if len(data) < 100:
        raise SystemExit(f"Evidence PNG is unexpectedly small: {name}")
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"Evidence file is not a PNG: {name}")
    width, height = struct.unpack(">II", data[16:24])
    if width <= 0 or height <= 0:
        raise SystemExit(f"Evidence PNG has invalid dimensions: {name}")
    artifacts[name] = {
        "bytes": len(data),
        "width": width,
        "height": height,
        "sha256": hashlib.sha256(data).hexdigest(),
    }

summary = {
    "status": "passed",
    "observed_frame_count": int(report.get("observed_frame_count", 0)),
    "metadata_match_count": int(report.get("metadata_match_count", 0)),
    "capture_count": int(report.get("capture_count", 0)),
    "rear_rotation_degrees": report.get("rear_rotation_degrees"),
    "rotated_rotation_degrees": report.get("rotated_rotation_degrees"),
    "front_rotation_degrees": report.get("front_rotation_degrees"),
    "last_capture_channel_range": [
        report.get("last_capture_minimum_channel"),
        report.get("last_capture_maximum_channel"),
    ],
    "artifacts": artifacts,
}
(report_dir / "summary.json").write_text(
    json.dumps(summary, indent=2) + "\n",
    encoding="utf-8",
)
print(json.dumps(summary, indent=2))
PY

(
    cd "${REPORT_DIR}"
    sha256sum \
        texture-final.json \
        rear-texture.png \
        rear-rotated-texture.png \
        front-texture.png \
        rear-screen.png \
        rear-rotated-screen.png \
        front-screen.png \
        > SHA256SUMS
)

printf '%s\n' 'RMA-092 CameraX GPU texture physical-device acceptance passed.'

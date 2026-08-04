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
STAGE_MARKER_GLOB="rma092-stage-*.txt"
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
        "${REMOTE_FILES_DIR}"/${STAGE_MARKER_GLOB} \
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
            local status
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

validate_texture_stage()
{
    local report_json="$1"
    local marker_text="$2"
    local expected_facing="$3"
    local capture_field="$4"
    local previous_rotation="${5:-}"
    python3 - "${report_json}" "${marker_text}" \
        "${expected_facing}" "${capture_field}" "${previous_rotation}" <<'PY'
from __future__ import annotations

import json
import re
import sys

report = json.loads(sys.argv[1])
marker_text = sys.argv[2]
expected_facing = sys.argv[3]
capture_field = sys.argv[4]
previous_rotation = sys.argv[5]


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

core_true = (
    "descriptor_monotonic",
    "timestamp_correspondence",
    "output_dimensions_valid",
    "mirror_contract_valid",
    "color_contract_valid",
)
if report.get("status") != "ok" or report.get("acceptance_enabled") is not True:
    raise SystemExit(1)
if report.get("bridge_state") != "Ready":
    raise SystemExit(1)
if integer("observed_frame_count") < 3 or integer("metadata_match_count") < 1:
    raise SystemExit(1)
if integer("stale_frame_count") != 0:
    raise SystemExit(1)
if any(report.get(field) is not True for field in core_true):
    raise SystemExit(1)

frame = report.get("frame") or {}
if frame.get("lens_facing") != expected_facing:
    raise SystemExit(1)
if int(frame.get("output_width", 0)) <= 0 or int(frame.get("output_height", 0)) <= 0:
    raise SystemExit(1)
expected_mirror = expected_facing == "Front"
if frame.get("mirrored") is not expected_mirror:
    raise SystemExit(1)
rotation = int(frame.get("rotation_degrees", -1))
if rotation not in {0, 90, 180, 270}:
    raise SystemExit(1)
if previous_rotation and rotation == int(previous_rotation):
    raise SystemExit(1)

# Prefer a real non-uniform live-camera capture whenever the unattended scene
# provides one. This remains the strongest physical evidence path.
if report.get(capture_field) is True:
    if report.get("captures_non_uniform") is not True:
        raise SystemExit(1)
    if report.get("captures_opaque") is not True:
        raise SystemExit(1)
    print(json.dumps({
        "mode": "live_camera_texture",
        "rotation_degrees": rotation,
        "frame_sequence": int(frame.get("sequence", 0)),
        "remote_capture_written": True,
    }, separators=(",", ":")))
    raise SystemExit(0)

# An unattended runner can legitimately point at a covered or dark surface.
# In that case, require all of the following instead of misclassifying correct
# black conversion as a broken GPU path:
#   * deterministic YUV->RGB shader probe passed on this physical GPU/API;
#   * live JNI/Texture2D planes are neutral limited-range black;
#   * the live RGB readback is correspondingly black;
#   * the marker sequence closely tracks the reported live frame.
lines = marker_text.splitlines()
if len(lines) < 2:
    raise SystemExit(1)
filename = lines[0].strip()
detail = " ".join(line.strip() for line in lines[1:] if line.strip())
name_match = re.fullmatch(
    r"rma092-stage-synth-(?P<synth>[01])-rgb-(?P<rgb_min>\d+)-(?P<rgb_max>\d+)-"
    r"opaque-(?P<opaque>[01])-y-(?P<y_min>\d+)-(?P<y_max>\d+)-"
    r"u-(?P<u_min>\d+)-(?P<u_max>\d+)-v-(?P<v_min>\d+)-(?P<v_max>\d+)-"
    r"api-(?P<api>[^/]+)\.txt",
    filename,
)
detail_match = re.search(r"live sequence=(\d+);", detail)
if name_match is None or detail_match is None:
    raise SystemExit(1)
values = {key: int(value) for key, value in name_match.groupdict().items()
          if key != "api"}
marker_sequence = int(detail_match.group(1))
frame_sequence = int(frame.get("sequence", 0))
if values["synth"] != 1 or values["opaque"] != 1:
    raise SystemExit(1)
if values["rgb_max"] - values["rgb_min"] < 128:
    raise SystemExit(1)
if values["y_max"] > 20 or values["y_max"] - values["y_min"] > 8:
    raise SystemExit(1)
for component in ("u_min", "u_max", "v_min", "v_max"):
    if not 96 <= values[component] <= 160:
        raise SystemExit(1)
if abs(frame_sequence - marker_sequence) > 10:
    raise SystemExit(1)
if integer("rejected_capture_count") < 1:
    raise SystemExit(1)
if integer("last_capture_minimum_channel") > 4:
    raise SystemExit(1)
if integer("last_capture_maximum_channel") > 4:
    raise SystemExit(1)

print(json.dumps({
    "mode": "dark_scene_synthetic_gpu_probe",
    "rotation_degrees": rotation,
    "frame_sequence": frame_sequence,
    "marker_sequence": marker_sequence,
    "graphics_api": name_match.group("api"),
    "synthetic_rgb_range": [values["rgb_min"], values["rgb_max"]],
    "live_y_range": [values["y_min"], values["y_max"]],
    "live_u_range": [values["u_min"], values["u_max"]],
    "live_v_range": [values["v_min"], values["v_max"]],
    "remote_capture_written": False,
}, separators=(",", ":")))
PY
}

wait_for_texture_stage()
{
    local description="$1"
    local report_destination="$2"
    local marker_destination="$3"
    local verdict_destination="$4"
    local expected_facing="$5"
    local capture_field="$6"
    local previous_rotation="${7:-}"
    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    local report_json=""
    local marker_text=""
    while true; do
        report_json="$(read_remote_file "${REMOTE_TEXTURE_RESULT}")"
        marker_text="$(read_stage_marker)"
        if [[ -n "${report_json}" && -n "${marker_text}" ]]; then
            if verdict="$(validate_texture_stage \
                    "${report_json}" \
                    "${marker_text}" \
                    "${expected_facing}" \
                    "${capture_field}" \
                    "${previous_rotation}" 2>/dev/null)"; then
                printf '%s\n' "${report_json}" > "${report_destination}"
                printf '%s\n' "${marker_text}" > "${marker_destination}"
                printf '%s\n' "${verdict}" > "${verdict_destination}"
                return
            fi
            local status
            status="$(json_field "${report_json}" status)"
            if [[ "${status}" == "error" ]]; then
                printf 'Texture evidence faulted while waiting for %s: %s\n' \
                    "${description}" "${report_json}" >&2
                return 1
            fi
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Timed out waiting for %s; last report: %s; stage marker: %s\n' \
                "${description}" "${report_json:-none}" "${marker_text:-none}" >&2
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

capture_stage_screen()
{
    local local_name="$1"
    "${ADB[@]}" exec-out screencap -p > "${REPORT_DIR}/${local_name}"
    test -s "${REPORT_DIR}/${local_name}"
}

pull_optional_camera_texture()
{
    local verdict_path="$1"
    local remote_name="$2"
    local local_name="$3"
    if python3 - "${verdict_path}" <<'PY'
import json
import sys
from pathlib import Path
value = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
raise SystemExit(0 if value.get("remote_capture_written") is True else 1)
PY
    then
        pull_remote_evidence "${remote_name}" "${local_name}"
    fi
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
wait_for_texture_stage \
    'the rear RGB texture evidence' \
    "${REPORT_DIR}/02-rear-texture.json" \
    "${REPORT_DIR}/02-rear-stage.txt" \
    "${REPORT_DIR}/02-rear-verdict.json" \
    Rear rear_capture_written
capture_stage_screen rear-screen.png
pull_optional_camera_texture \
    "${REPORT_DIR}/02-rear-verdict.json" \
    "${REAR_TEXTURE_FILE}" \
    rear-texture.png
REAR_ROTATION="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["rotation_degrees"])' "${REPORT_DIR}/02-rear-verdict.json")"

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
wait_for_texture_stage \
    'the rotated rear RGB texture evidence' \
    "${REPORT_DIR}/05-rear-rotated-texture.json" \
    "${REPORT_DIR}/05-rear-rotated-stage.txt" \
    "${REPORT_DIR}/05-rear-rotated-verdict.json" \
    Rear rotated_capture_written "${REAR_ROTATION}"
capture_stage_screen rear-rotated-screen.png
pull_optional_camera_texture \
    "${REPORT_DIR}/05-rear-rotated-verdict.json" \
    "${ROTATED_TEXTURE_FILE}" \
    rear-rotated-texture.png

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
wait_for_texture_stage \
    'the mirrored front RGB texture evidence' \
    "${REPORT_DIR}/08-front-texture.json" \
    "${REPORT_DIR}/08-front-stage.txt" \
    "${REPORT_DIR}/08-front-verdict.json" \
    Front front_capture_written
capture_stage_screen front-screen.png
pull_optional_camera_texture \
    "${REPORT_DIR}/08-front-verdict.json" \
    "${FRONT_TEXTURE_FILE}" \
    front-texture.png

pull_remote_evidence "${TEXTURE_RESULT_FILE}" "texture-final.json"
capture_diagnostics

python3 - "${REPORT_DIR}" <<'PY'
from __future__ import annotations

import hashlib
import json
import struct
import sys
from pathlib import Path

report_dir = Path(sys.argv[1])
stages = []
for prefix in ("02-rear", "05-rear-rotated", "08-front"):
    report = json.loads((report_dir / f"{prefix}-texture.json").read_text(encoding="utf-8"))
    verdict = json.loads((report_dir / f"{prefix}-verdict.json").read_text(encoding="utf-8"))
    marker = (report_dir / f"{prefix}-stage.txt").read_text(encoding="utf-8")
    if report.get("status") != "ok" or report.get("bridge_state") != "Ready":
        raise SystemExit(f"RMA-092 stage {prefix} is unhealthy: {report}")
    if int(report.get("stale_frame_count", 0)) != 0:
        raise SystemExit(f"RMA-092 stage {prefix} accepted a stale frame: {report}")
    if report.get("timestamp_correspondence") is not True:
        raise SystemExit(f"RMA-092 stage {prefix} lost timestamp correspondence: {report}")
    if verdict.get("mode") not in {
        "live_camera_texture",
        "dark_scene_synthetic_gpu_probe",
    }:
        raise SystemExit(f"RMA-092 stage {prefix} has no valid evidence mode: {verdict}")
    if not marker.startswith("rma092-stage-synth-1-"):
        raise SystemExit(f"RMA-092 stage {prefix} lacks a passing physical GPU marker: {marker}")
    stages.append({
        "name": prefix,
        "mode": verdict["mode"],
        "rotation_degrees": int(verdict["rotation_degrees"]),
        "frame_sequence": int(verdict["frame_sequence"]),
        "graphics_api": verdict.get("graphics_api", "live-camera-capture"),
        "live_y_range": verdict.get("live_y_range"),
        "synthetic_rgb_range": verdict.get("synthetic_rgb_range"),
    })

if stages[0]["rotation_degrees"] == stages[1]["rotation_degrees"]:
    raise SystemExit(f"Display rotation did not change RGB texture orientation: {stages}")
front_report = json.loads((report_dir / "08-front-texture.json").read_text(encoding="utf-8"))
front_frame = front_report.get("frame") or {}
if front_frame.get("lens_facing") != "Front" or front_frame.get("mirrored") is not True:
    raise SystemExit(f"The final front texture is not marked mirrored: {front_report}")

artifacts = {}
for name in (
    "rear-screen.png",
    "rear-rotated-screen.png",
    "front-screen.png",
):
    path = report_dir / name
    data = path.read_bytes()
    if len(data) < 100 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"Invalid device-screen PNG: {name}")
    width, height = struct.unpack(">II", data[16:24])
    if width <= 0 or height <= 0:
        raise SystemExit(f"Evidence PNG has invalid dimensions: {name}")
    artifacts[name] = {
        "bytes": len(data),
        "width": width,
        "height": height,
        "sha256": hashlib.sha256(data).hexdigest(),
    }

for name in (
    "rear-texture.png",
    "rear-rotated-texture.png",
    "front-texture.png",
):
    path = report_dir / name
    if not path.exists():
        continue
    data = path.read_bytes()
    if len(data) < 100 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"Invalid live texture PNG: {name}")
    width, height = struct.unpack(">II", data[16:24])
    artifacts[name] = {
        "bytes": len(data),
        "width": width,
        "height": height,
        "sha256": hashlib.sha256(data).hexdigest(),
    }

summary = {
    "status": "passed",
    "contract": (
        "live non-uniform captures are preferred; neutral limited-range dark "
        "scenes require a passing deterministic physical-GPU shader probe and "
        "a matching black live RGB readback"
    ),
    "stages": stages,
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
    mapfile -t checksum_files < <(
        find . -maxdepth 1 -type f \
            \( -name '*.json' -o -name '*.png' -o -name '*-stage.txt' \) \
            -printf '%f\n' \
            | sort
    )
    sha256sum "${checksum_files[@]}" > SHA256SUMS
)

printf '%s\n' 'RMA-092 CameraX GPU texture physical-device acceptance passed.'

#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
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

# run_rma092_camera_texture_acceptance_android.sh was split (docs/
# LARGE_FILE_REFACTOR_TODO_3.md-style refactor, performed after round 3
# closed) into this entrypoint plus two sourced function libraries. Source
# both before anything below calls select_device_serial/cleanup/etc.
# shellcheck source=scripts/run_rma092_camera_texture_acceptance_android_device.sh
source "${SCRIPT_DIR}/run_rma092_camera_texture_acceptance_android_device.sh"
# shellcheck source=scripts/run_rma092_camera_texture_acceptance_android_evidence.sh
source "${SCRIPT_DIR}/run_rma092_camera_texture_acceptance_android_evidence.sh"

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

trap cleanup EXIT

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
    'CameraX stop before a forced device-rotation attempt' \
    "${REPORT_DIR}/03-rotation-stopped.json" \
    acquisition_stopped rotation-stop
# The app is locked to portrait (see AndroidBuild.ConfigureMobileOrientation). This
# still forces the system-level display rotation the same way a physical device
# rotation would, then proves the RGB texture orientation does not change -- the lock
# holding under a real rotation attempt, not merely the absence of one.
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
    'the rear CameraX session after a forced device-rotation attempt' \
    "${REPORT_DIR}/04-rear-rotated-running.json" \
    acquisition_running rear-rotated Rear
wait_for_texture_stage \
    'the rear RGB texture evidence after a forced device-rotation attempt' \
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

if stages[0]["rotation_degrees"] != stages[1]["rotation_degrees"]:
    raise SystemExit(
        f"Portrait lock did not hold RGB texture orientation fixed under a forced "
        f"device-rotation attempt: {stages}"
    )
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

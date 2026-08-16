#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA091_CAMERA_REPORT_DIR:-${ROOT_DIR}/build/rma091-camera-device-report}"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
PACKAGE_NAME="com.ekkus.weachymini"
RESULT_FILE_NAME="rma091-camera-acquisition-state.json"
COMMAND_FILE_NAME="rma091-camera-acquisition-command.json"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT_PATH="${REMOTE_FILES_DIR}/${RESULT_FILE_NAME}"
REMOTE_COMMAND_PATH="${REMOTE_FILES_DIR}/${COMMAND_FILE_NAME}"
TIMEOUT_SECONDS="${RMA091_CAMERA_TIMEOUT_SECONDS:-60}"
POLL_SECONDS="${RMA091_CAMERA_POLL_SECONDS:-0.5}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! -s "${FOREGROUND_HELPER}" ]]; then
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
fi
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'Camera acquisition timeout must be a positive integer: %s\n' \
        "${TIMEOUT_SECONDS}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null
command -v python3 >/dev/null

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

read_device_report()
{
    "${ADB[@]}" shell \
        "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" \
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
    "${ADB[@]}" shell dumpsys window policy > "${REPORT_DIR}/window-policy.txt"
    "${ADB[@]}" shell dumpsys power > "${REPORT_DIR}/power.txt"
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt"
    "${ADB[@]}" shell dumpsys display > "${REPORT_DIR}/display.txt"
    "${ADB[@]}" shell \
        "ls -laR '${REMOTE_FILES_DIR}' 2>&1" \
        > "${REPORT_DIR}/external-files.txt"
    read_device_report > "${REPORT_DIR}/camera-acquisition-latest.json"
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
    if [[ "${ORIGINAL_ACCELEROMETER_ROTATION}" =~ ^[01]$ ]]; then
        "${ADB[@]}" shell settings put system accelerometer_rotation \
            "${ORIGINAL_ACCELEROMETER_ROTATION}" >/dev/null 2>&1
    fi
    if [[ "${ORIGINAL_USER_ROTATION}" =~ ^[0-3]$ ]]; then
        "${ADB[@]}" shell settings put system user_rotation \
            "${ORIGINAL_USER_ROTATION}" >/dev/null 2>&1
    fi
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
    "${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA \
        >/dev/null 2>&1
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10
    exit "${exit_code}"
}
trap cleanup EXIT

remove_remote_evidence()
{
    "${ADB[@]}" shell rm -f \
        "${REMOTE_RESULT_PATH}" \
        "${REMOTE_RESULT_PATH}.tmp" \
        "${REMOTE_COMMAND_PATH}" \
        "${REMOTE_COMMAND_PATH}.tmp" \
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
        > "${REPORT_DIR}/launch-${suffix}.txt"
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30 \
        > "${REPORT_DIR}/focus-${suffix}.txt"
}

bring_application_to_foreground()
{
    local suffix="$1"
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
        > "${REPORT_DIR}/prepare-${suffix}.txt"
    "${ADB[@]}" shell am start -W \
        -n "${LAUNCH_COMPONENT}" \
        -a android.intent.action.MAIN \
        -c android.intent.category.LAUNCHER \
        --ez reachy_rma091_acceptance true \
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
    "${ADB[@]}" push "${local_path}" "${REMOTE_COMMAND_PATH}.tmp" \
        > "${REPORT_DIR}/push-${command_id}.txt"
    "${ADB[@]}" shell mv \
        "${REMOTE_COMMAND_PATH}.tmp" \
        "${REMOTE_COMMAND_PATH}"
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

def common_frame_contract() -> bool:
    frame = report.get("frame") or {}
    return (
        report.get("status") == "ok"
        and report.get("acceptance_enabled") is True
        and report.get("has_frame") is True
        and report.get("metadata_monotonic") is True
        and report.get("all_frames_yuv420888") is True
        and report.get("all_frames_valid_crop") is True
        and report.get("all_frames_valid_intrinsics") is True
        and report.get("all_frames_positive_timestamp") is True
        and frame.get("pixel_format") == "Yuv420888"
        and integer("frame.timestamp_nanoseconds") > 0
        and integer("frame.width") > 0
        and integer("frame.height") > 0
        and integer("frame.crop_right") > integer("frame.crop_left")
        and integer("frame.crop_bottom") > integer("frame.crop_top")
        and float(frame.get("focal_length_x", 0.0)) > 0.0
        and float(frame.get("focal_length_y", 0.0)) > 0.0
        and bool(frame.get("intrinsics_provenance"))
    )

matched = False
if condition == "ready":
    expected_permission = args[0]
    matched = (
        report.get("status") == "ok"
        and report.get("acceptance_enabled") is True
        and report.get("permission") == expected_permission
        and report.get("current_state") in {
            "Stopped", "PermissionRevoked", "Unavailable"
        }
    )
elif condition == "running":
    command_id, facing, minimum_frames = args
    matched = (
        report.get("last_command_id") == command_id
        and report.get("last_command_status") == "ok"
        and report.get("current_state") == "Running"
        and report.get("requested_facing") == facing
        and (report.get("frame") or {}).get("lens_facing") == facing
        and integer("accepted_frame_count") >= int(minimum_frames)
        and common_frame_contract()
    )
elif condition == "progress":
    baseline, delta = map(int, args)
    matched = (
        report.get("current_state") == "Running"
        and integer("accepted_frame_count") >= baseline + delta
        and common_frame_contract()
    )
elif condition == "state":
    expected_state = args[0]
    command_id = args[1] if len(args) > 1 else ""
    matched = report.get("current_state") == expected_state
    if command_id:
        matched = matched and report.get("last_command_id") == command_id
elif condition == "resumed":
    matched = (
        report.get("current_state") == "Running"
        and integer("paused_transition_count") >= 1
        and integer("resumed_transition_count") >= 1
        and integer("application_pause_count") >= 1
        and integer("application_resume_count") >= 1
        and common_frame_contract()
    )
elif condition == "rotation_unaffected":
    # The app is locked to portrait, so a forced system display-rotation attempt must
    # not change the camera frame's reported rotation: Android does not rotate a
    # fixed-orientation activity's window, and CameraX derives frame.rotation_degrees
    # from that window's display rotation. This proves the lock actually holds under a
    # real rotation attempt, not merely that nothing asked for one.
    command_id, original_rotation = args
    matched = (
        report.get("last_command_id") == command_id
        and report.get("current_state") == "Running"
        and integer("frame.rotation_degrees") == int(original_rotation)
        and common_frame_contract()
    )
elif condition == "revoked":
    command_id = args[0]
    matched = (
        report.get("permission") == "Revoked"
        and report.get("last_command_id") == command_id
        and report.get("current_state") == "PermissionRevoked"
        and report.get("desired_active") is False
        and report.get("has_frame") is False
        and integer("current_session_id") == 0
        and integer("accepted_frame_count") == 0
    )

raise SystemExit(0 if matched else 1)
PY
}

wait_for_report()
{
    local description="$1"
    local destination="$2"
    local condition="$3"
    shift 3
    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    local report_json=""
    while true; do
        report_json="$(read_device_report)"
        if [[ -n "${report_json}" ]]; then
            printf '%s\n' "${report_json}" \
                > "${REPORT_DIR}/camera-acquisition-latest.json"
            if report_matches "${report_json}" "${condition}" "$@"; then
                printf '%s\n' "${report_json}" > "${destination}"
                return
            fi
            state="$(json_field "${report_json}" current_state)"
            if [[ "${state}" == "Faulted" ]]; then
                printf 'Camera acquisition faulted while waiting for %s: %s\n' \
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
    'the opt-in evidence service and granted discovery state' \
    "${REPORT_DIR}/00-ready.json" \
    ready Granted

write_command rear-1 start rear
wait_for_report \
    'the first rear CameraX session to deliver valid frames' \
    "${REPORT_DIR}/01-rear-running.json" \
    running rear-1 Rear 5
REAR_BASELINE="$(
    json_field "$(cat "${REPORT_DIR}/01-rear-running.json")" accepted_frame_count
)"
wait_for_report \
    'keep-only-latest analysis to continue making progress' \
    "${REPORT_DIR}/02-analyzer-progress.json" \
    progress "${REAR_BASELINE}" 10

"${ADB[@]}" shell input keyevent 3
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    wait-background "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
    > "${REPORT_DIR}/focus-background.txt"
wait_for_report \
    'CameraX lifecycle pause after application backgrounding' \
    "${REPORT_DIR}/03-paused.json" \
    state Paused
bring_application_to_foreground resumed
wait_for_report \
    'CameraX lifecycle resume with frame delivery restored' \
    "${REPORT_DIR}/04-resumed.json" \
    resumed

write_command stop-1 stop
wait_for_report \
    'the first explicit CameraX stop' \
    "${REPORT_DIR}/05-stopped.json" \
    state Stopped stop-1
write_command rear-2 start rear
wait_for_report \
    'the second rear CameraX session' \
    "${REPORT_DIR}/06-rear-restarted.json" \
    running rear-2 Rear 5
ORIGINAL_FRAME_ROTATION="$(
    json_field "$(cat "${REPORT_DIR}/06-rear-restarted.json")" \
        frame.rotation_degrees
)"

write_command rotation-stop stop
wait_for_report \
    'CameraX stop before a forced device-rotation attempt' \
    "${REPORT_DIR}/07-rotation-stopped.json" \
    state Stopped rotation-stop
# The app is locked to portrait (see AndroidBuild.ConfigureMobileOrientation). This
# still forces the system-level display rotation the same way a physical device
# rotation would, then proves CameraX's reported frame rotation does not change and
# streaming is not disrupted -- the lock holding under a real rotation attempt, not
# merely the absence of one.
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
    'the rear CameraX session after a forced device-rotation attempt' \
    "${REPORT_DIR}/08-rear-rotated.json" \
    running rear-rotated Rear 5
wait_for_report \
    'CameraX rotation metadata to stay fixed under the portrait lock' \
    "${REPORT_DIR}/09-rotation-changed.json" \
    rotation_unaffected rear-rotated "${ORIGINAL_FRAME_ROTATION}"

write_command switch-stop stop
wait_for_report \
    'CameraX stop before front-camera switch' \
    "${REPORT_DIR}/10-switch-stopped.json" \
    state Stopped switch-stop
write_command front-1 start front
wait_for_report \
    'front CameraX session after rear-camera sessions' \
    "${REPORT_DIR}/11-front-running.json" \
    running front-1 Front 5
cp "${REPORT_DIR}/11-front-running.json" \
    "${REPORT_DIR}/12-pre-revoke-cumulative.json"

"${ADB[@]}" shell input keyevent 3
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    wait-background "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
    > "${REPORT_DIR}/focus-pre-revoke-background.txt"
"${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA \
    > "${REPORT_DIR}/revoke.txt"
"${ADB[@]}" shell rm -f \
    "${REMOTE_COMMAND_PATH}" \
    "${REMOTE_COMMAND_PATH}.tmp" \
    >/dev/null 2>&1 || true
bring_application_to_foreground revoked
wait_for_report \
    'the evidence service after camera permission revocation' \
    "${REPORT_DIR}/13-revoked-ready.json" \
    ready Revoked
write_command revoked-start start rear
wait_for_report \
    'fail-closed acquisition after camera permission revocation' \
    "${REPORT_DIR}/14-permission-revoked.json" \
    revoked revoked-start

capture_diagnostics
python3 - "${REPORT_DIR}" <<'PY'
from __future__ import annotations

import json
import sys
from pathlib import Path

report_dir = Path(sys.argv[1])

def load(name: str) -> dict:
    return json.loads((report_dir / name).read_text(encoding="utf-8"))

def integer(report: dict, key: str) -> int:
    return int(report.get(key, 0))

rear = load("01-rear-running.json")
progress = load("02-analyzer-progress.json")
paused = load("03-paused.json")
resumed = load("04-resumed.json")
rear_restarted = load("06-rear-restarted.json")
rotated = load("09-rotation-changed.json")
front = load("11-front-running.json")
pre_revoke = load("12-pre-revoke-cumulative.json")
revoked = load("14-permission-revoked.json")

for name, report in {
    "rear": rear,
    "progress": progress,
    "resumed": resumed,
    "rear_restarted": rear_restarted,
    "rotated": rotated,
    "front": front,
    "pre_revoke": pre_revoke,
}.items():
    if report.get("status") != "ok":
        raise SystemExit(f"{name} evidence is not healthy: {report}")
    for field in (
        "metadata_monotonic",
        "all_frames_yuv420888",
        "all_frames_valid_crop",
        "all_frames_valid_intrinsics",
        "all_frames_positive_timestamp",
    ):
        if report.get(field) is not True:
            raise SystemExit(f"{name} failed {field}: {report}")
    if integer(report, "stale_frame_count") != 0:
        raise SystemExit(f"{name} accepted stale frame metadata: {report}")
    if integer(report, "faulted_transition_count") != 0:
        raise SystemExit(f"{name} entered a faulted state: {report}")

if rear.get("current_state") != "Running" or not rear.get("rear_frame_seen"):
    raise SystemExit(f"Rear acquisition did not run: {rear}")
if integer(progress, "accepted_frame_count") < integer(rear, "accepted_frame_count") + 10:
    raise SystemExit(f"Keep-only-latest analysis stopped progressing: {progress}")
if paused.get("current_state") != "Paused":
    raise SystemExit(f"Backgrounding did not pause CameraX: {paused}")
if integer(resumed, "paused_transition_count") < 1 or integer(resumed, "resumed_transition_count") < 1:
    raise SystemExit(f"CameraX pause/resume transitions were not observed: {resumed}")
if integer(rear_restarted, "session_count") < 2:
    raise SystemExit(f"Repeated start/stop did not create a new session: {rear_restarted}")
if (rotated.get("frame") or {}).get("rotation_degrees") != (rear_restarted.get("frame") or {}).get("rotation_degrees"):
    raise SystemExit(f"Portrait lock did not hold under a forced device-rotation attempt: {rotated}")
if front.get("current_state") != "Running" or not front.get("front_frame_seen"):
    raise SystemExit(f"Front acquisition did not run: {front}")
if not front.get("rear_frame_seen"):
    raise SystemExit(f"Rear-to-front switch lost cumulative rear evidence: {front}")
if integer(pre_revoke, "session_count") < 4:
    raise SystemExit(f"Expected at least four CameraX sessions: {pre_revoke}")
if integer(pre_revoke, "start_command_count") < 4 or integer(pre_revoke, "stop_command_count") < 3:
    raise SystemExit(f"Repeated CameraX start/stop coverage is incomplete: {pre_revoke}")
if revoked.get("permission") != "Revoked" or revoked.get("current_state") != "PermissionRevoked":
    raise SystemExit(f"Permission revocation did not fail closed: {revoked}")
if revoked.get("desired_active") is not False or revoked.get("has_frame") is not False:
    raise SystemExit(f"Revoked acquisition retained active frame state: {revoked}")

summary = {
    "status": "passed",
    "rear_camera_id": rear.get("camera_id"),
    "front_camera_id": front.get("camera_id"),
    "rear_initial_frames": integer(rear, "accepted_frame_count"),
    "rear_progress_frames": integer(progress, "accepted_frame_count"),
    "session_count": integer(pre_revoke, "session_count"),
    "frame_observation_count": integer(pre_revoke, "frame_observation_count"),
    "initial_rotation_degrees": (rear_restarted.get("frame") or {}).get("rotation_degrees"),
    "rotation_degrees_after_forced_rotation_attempt": (rotated.get("frame") or {}).get("rotation_degrees"),
    "permission_revocation_state": revoked.get("current_state"),
}
(report_dir / "summary.json").write_text(
    json.dumps(summary, indent=2) + "\n",
    encoding="utf-8",
)
print(json.dumps(summary, indent=2))
PY

printf '%s\n' 'RMA-091 CameraX physical-device acceptance passed.'

#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA190_PERMISSION_REPORT_DIR:-${ROOT_DIR}/build/rma190-permission-end-to-end-report}"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
PACKAGE_NAME="com.ekkus.weachymini"
RESULT_FILE_NAME="rma091-camera-acquisition-state.json"
COMMAND_FILE_NAME="rma091-camera-acquisition-command.json"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT_PATH="${REMOTE_FILES_DIR}/${RESULT_FILE_NAME}"
REMOTE_COMMAND_PATH="${REMOTE_FILES_DIR}/${COMMAND_FILE_NAME}"
REMOTE_UI_DUMP="/sdcard/rma190-permission-window.xml"
TIMEOUT_SECONDS="${RMA190_PERMISSION_TIMEOUT_SECONDS:-60}"
POLL_SECONDS="${RMA190_PERMISSION_POLL_SECONDS:-0.5}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! -s "${FOREGROUND_HELPER}" ]]; then
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
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

read_device_report()
{
    "${ADB[@]}" shell \
        "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" \
        2>/dev/null | tr -d '\r' || true
}

capture_diagnostics()
{
    set +e
    "${ADB[@]}" logcat -d -v threadtime > "${REPORT_DIR}/logcat.txt"
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt"
    "${ADB[@]}" shell dumpsys window windows > "${REPORT_DIR}/window.txt"
    "${ADB[@]}" exec-out screencap -p > "${REPORT_DIR}/device-screen.png"
    read_device_report > "${REPORT_DIR}/permission-latest.json"
    "${ADB[@]}" shell uiautomator dump "${REMOTE_UI_DUMP}" \
        > "${REPORT_DIR}/uiautomator-final.txt" 2>&1 || true
    "${ADB[@]}" shell cat "${REMOTE_UI_DUMP}" \
        > "${REPORT_DIR}/window-final.xml" 2>/dev/null || true
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
    "${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA \
        >/dev/null 2>&1
    "${ADB[@]}" shell rm -f "${REMOTE_UI_DUMP}" >/dev/null 2>&1
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
        --ez reachy_rma190_permission_e2e_acceptance true \
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
        --ez reachy_rma190_permission_e2e_acceptance true \
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
import json
import sys
print(json.dumps({"id": sys.argv[1], "action": sys.argv[2], "facing": sys.argv[3]}, separators=(",", ":")))
PY
    "${ADB[@]}" shell mkdir -p "${REMOTE_FILES_DIR}"
    "${ADB[@]}" push "${local_path}" "${REMOTE_COMMAND_PATH}.tmp" \
        > "${REPORT_DIR}/push-${command_id}.txt"
    "${ADB[@]}" shell mv "${REMOTE_COMMAND_PATH}.tmp" "${REMOTE_COMMAND_PATH}"
}

report_matches()
{
    local report_json="$1"
    local condition="$2"
    shift 2
    python3 - "${report_json}" "${condition}" "$@" <<'PY'
import json
import sys
report=json.loads(sys.argv[1]); condition=sys.argv[2]; args=sys.argv[3:]
def integer(key):
    try: return int(report.get(key, 0))
    except (TypeError, ValueError): return 0
if condition == "permission":
    matched = report.get("status") == "ok" and report.get("permission") == args[0]
elif condition == "denied_fail_closed":
    matched = (
        report.get("status") == "ok"
        and report.get("permission") == "Denied"
        and report.get("last_command_id") == args[0]
        and report.get("current_state") == "Unavailable"
        and report.get("desired_active") is False
        and report.get("has_frame") is False
        and integer("current_session_id") == 0
        and integer("accepted_frame_count") == 0
        and integer("stale_frame_count") == 0
    )
elif condition == "running":
    matched = (
        report.get("status") == "ok"
        and report.get("permission") == "Granted"
        and report.get("last_command_id") == args[0]
        and report.get("current_state") == "Running"
        and report.get("desired_active") is True
        and report.get("has_frame") is True
        and integer("current_session_id") > 0
        and integer("accepted_frame_count") >= int(args[1])
        and integer("stale_frame_count") == 0
    )
elif condition == "revoked_fail_closed":
    matched = (
        report.get("status") == "ok"
        and report.get("permission") == "Revoked"
        and report.get("last_command_id") == args[0]
        and report.get("current_state") == "PermissionRevoked"
        and report.get("desired_active") is False
        and report.get("has_frame") is False
        and integer("current_session_id") == 0
        and integer("accepted_frame_count") == 0
        and integer("stale_frame_count") == 0
    )
else:
    matched = False
raise SystemExit(0 if matched else 1)
PY
}

wait_for_report()
{
    local description="$1" destination="$2" condition="$3"
    shift 3
    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS )) report_json=""
    while true; do
        report_json="$(read_device_report)"
        if [[ -n "${report_json}" ]]; then
            printf '%s\n' "${report_json}" > "${REPORT_DIR}/permission-latest.json"
            if report_matches "${report_json}" "${condition}" "$@"; then
                printf '%s\n' "${report_json}" > "${destination}"
                return
            fi
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Timed out waiting for %s; last report: %s\n' \
                "${description}" "${report_json:-none}" >&2
            return 1
        fi
        if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
            printf 'Unity application exited while waiting for %s.\n' "${description}" >&2
            return 1
        fi
        sleep "${POLL_SECONDS}"
    done
}

click_permission_deny()
{
    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS )) attempt=0
    while true; do
        attempt=$((attempt + 1))
        "${ADB[@]}" shell rm -f "${REMOTE_UI_DUMP}" >/dev/null 2>&1 || true
        "${ADB[@]}" shell uiautomator dump "${REMOTE_UI_DUMP}" \
            > "${REPORT_DIR}/uiautomator-deny-${attempt}.txt" 2>&1 || true
        local xml coordinates
        xml="$("${ADB[@]}" shell cat "${REMOTE_UI_DUMP}" 2>/dev/null | tr -d '\r' || true)"
        printf '%s\n' "${xml}" > "${REPORT_DIR}/permission-dialog-${attempt}.xml"
        coordinates="$(python3 - "${xml}" <<'PY'
import re
import sys
import xml.etree.ElementTree as ET
xml=sys.argv[1]
try:
    root=ET.fromstring(xml)
except ET.ParseError:
    raise SystemExit(0)
for node in root.iter("node"):
    text=(node.attrib.get("text") or "").replace("’", "'").strip().lower()
    resource=(node.attrib.get("resource-id") or "").lower()
    if "permission_deny_button" not in resource and text not in {"don't allow", "deny"}:
        continue
    match=re.fullmatch(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", node.attrib.get("bounds", ""))
    if match:
        x1,y1,x2,y2=map(int, match.groups())
        print(f"{(x1+x2)//2} {(y1+y2)//2}")
        break
PY
)"
        if [[ "${coordinates}" =~ ^[0-9]+[[:space:]][0-9]+$ ]]; then
            read -r tap_x tap_y <<< "${coordinates}"
            printf 'deny_button_coordinates=%s,%s\n' "${tap_x}" "${tap_y}" \
                > "${REPORT_DIR}/permission-deny-click.txt"
            "${ADB[@]}" shell input tap "${tap_x}" "${tap_y}"
            return
        fi
        if (( $(date +%s) >= deadline )); then
            printf '%s\n' 'Timed out waiting for the Android camera permission denial button.' >&2
            return 1
        fi
        sleep "${POLL_SECONDS}"
    done
}

assert_camera_permission_grant_state()
{
    local expected="$1" destination="$2"
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${destination}"
    if [[ "${expected}" == "true" ]]; then
        grep -Eq '^[[:space:]]*android\.permission\.CAMERA: granted=true' "${destination}"
    else
        if grep -Eq '^[[:space:]]*android\.permission\.CAMERA: granted=true' "${destination}"; then
            printf '%s\n' 'Android reports CAMERA granted when fail-closed acceptance requires it denied.' >&2
            return 1
        fi
    fi
}

{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'manufacturer=%s\n' "$("${ADB[@]}" shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' "$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
} > "${REPORT_DIR}/device.txt"

"${ADB[@]}" install -r "${APK_PATH}" > "${REPORT_DIR}/install.txt"
LAUNCH_COMPONENT="$(
    "${ADB[@]}" shell cmd package resolve-activity --brief \
        -a android.intent.action.MAIN \
        -c android.intent.category.LAUNCHER \
        "${PACKAGE_NAME}" | tr -d '\r' | tail -n 1
)"
if [[ -z "${LAUNCH_COMPONENT}" || "${LAUNCH_COMPONENT}" != */* ]]; then
    printf 'Could not resolve launcher activity: %s\n' "${LAUNCH_COMPONENT}" >&2
    exit 1
fi

"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/clear.txt"
"${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA \
    > "${REPORT_DIR}/initial-revoke.txt" 2>&1 || true
remove_remote_evidence
"${ADB[@]}" logcat -c || true
launch_application initial
wait_for_report 'initial NotRequested state' "${REPORT_DIR}/00-not-requested.json" permission NotRequested
assert_camera_permission_grant_state false "${REPORT_DIR}/package-not-requested.txt"

write_command request-deny request_permission
click_permission_deny
wait_for_report 'real Android denial callback' "${REPORT_DIR}/01-denied.json" permission Denied
assert_camera_permission_grant_state false "${REPORT_DIR}/package-denied.txt"
write_command denied-start start rear
wait_for_report 'fail-closed acquisition after denial' "${REPORT_DIR}/02-denied-fail-closed.json" denied_fail_closed denied-start

"${ADB[@]}" shell pm grant "${PACKAGE_NAME}" android.permission.CAMERA > "${REPORT_DIR}/grant.txt"
write_command grant-refresh request_permission
wait_for_report 'granted discovery after explicit grant' "${REPORT_DIR}/03-granted.json" permission Granted
assert_camera_permission_grant_state true "${REPORT_DIR}/package-granted.txt"
write_command rear-running start rear
wait_for_report 'real CameraX frames before revocation' "${REPORT_DIR}/04-running.json" running rear-running 5

"${ADB[@]}" shell input keyevent 3
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    wait-background "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
    > "${REPORT_DIR}/focus-background.txt"
"${ADB[@]}" shell pm revoke "${PACKAGE_NAME}" android.permission.CAMERA > "${REPORT_DIR}/revoke.txt"
assert_camera_permission_grant_state false "${REPORT_DIR}/package-revoked.txt"
bring_application_to_foreground revoked
wait_for_report 'revoked discovery state' "${REPORT_DIR}/05-revoked.json" permission Revoked
write_command revoked-start start rear
wait_for_report 'fail-closed acquisition after revocation' "${REPORT_DIR}/06-revoked-fail-closed.json" revoked_fail_closed revoked-start

capture_diagnostics
python3 - "${REPORT_DIR}" <<'PY'
import json
import sys
from pathlib import Path
p=Path(sys.argv[1])
def load(name): return json.loads((p/name).read_text(encoding="utf-8"))
initial=load("00-not-requested.json")
denied=load("01-denied.json")
denied_closed=load("02-denied-fail-closed.json")
granted=load("03-granted.json")
running=load("04-running.json")
revoked=load("05-revoked.json")
revoked_closed=load("06-revoked-fail-closed.json")
if initial.get("permission") != "NotRequested" or int(initial.get("permission_request_count",0)) != 0:
    raise SystemExit(f"Initial permission state was not clean: {initial}")
if denied.get("permission") != "Denied" or int(denied.get("permission_request_count",0)) != 1:
    raise SystemExit(f"Real permission denial was not recorded exactly once: {denied}")
for name, report, expected_state in (
    ("denied", denied_closed, "Unavailable"),
    ("revoked", revoked_closed, "PermissionRevoked"),
):
    if report.get("current_state") != expected_state:
        raise SystemExit(f"{name} state did not fail closed: {report}")
    if report.get("desired_active") is not False or report.get("has_frame") is not False:
        raise SystemExit(f"{name} retained active camera/frame state: {report}")
    if int(report.get("current_session_id",0)) != 0 or int(report.get("accepted_frame_count",0)) != 0:
        raise SystemExit(f"{name} retained session/frame counters: {report}")
    if int(report.get("stale_frame_count",0)) != 0:
        raise SystemExit(f"{name} accepted stale camera metadata: {report}")
if granted.get("permission") != "Granted":
    raise SystemExit(f"Explicit grant did not restore discovery: {granted}")
if running.get("current_state") != "Running" or running.get("has_frame") is not True:
    raise SystemExit(f"Real CameraX session did not run before revocation: {running}")
if int(running.get("accepted_frame_count",0)) < 5:
    raise SystemExit(f"Insufficient real frames before revocation: {running}")
if revoked.get("permission") != "Revoked":
    raise SystemExit(f"Revocation was not observed by shipping discovery: {revoked}")
summary={
    "status":"passed",
    "physical_android":True,
    "real_android_permission_dialog_denied":True,
    "initial_permission":initial.get("permission"),
    "denied_permission":denied.get("permission"),
    "denied_permission_request_count":int(denied.get("permission_request_count",0)),
    "denied_acquisition_state":denied_closed.get("current_state"),
    "real_camera_frames_before_revocation":int(running.get("accepted_frame_count",0)),
    "revoked_permission":revoked.get("permission"),
    "revoked_acquisition_state":revoked_closed.get("current_state"),
    "stale_frame_count_after_denial":int(denied_closed.get("stale_frame_count",0)),
    "stale_frame_count_after_revocation":int(revoked_closed.get("stale_frame_count",0)),
    "synthetic_permission_state_injected":False,
    "synthetic_camera_frame_injected":False,
}
(p/"summary.json").write_text(json.dumps(summary,indent=2)+"\n",encoding="utf-8")
print(json.dumps(summary,indent=2))
PY

printf '%s\n' 'RMA-190 permission denial/revocation physical-device acceptance passed.'

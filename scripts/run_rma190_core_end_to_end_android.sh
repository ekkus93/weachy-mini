#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA190_CORE_REPORT_DIR:-${ROOT_DIR}/build/rma190-core-end-to-end-report}"
PACKAGE_NAME="com.ekkus.weachymini"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
RESULT_FILE="rma190-core-end-to-end-acceptance.json"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT="${REMOTE_FILES_DIR}/${RESULT_FILE}"
TIMEOUT_SECONDS="${RMA190_CORE_TIMEOUT_SECONDS:-150}"
POLL_SECONDS="${RMA190_CORE_POLL_SECONDS:-0.5}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'RMA-190 timeout must be a positive integer: %s\n' \
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

wifi_before="$("${ADB[@]}" shell settings get global wifi_on 2>/dev/null | tr -d '\r' || true)"
data_before="$("${ADB[@]}" shell settings get global mobile_data 2>/dev/null | tr -d '\r' || true)"

restore_network()
{
    set +e
    if [[ "${wifi_before}" == "1" ]]; then
        "${ADB[@]}" shell svc wifi enable >/dev/null 2>&1
    else
        "${ADB[@]}" shell svc wifi disable >/dev/null 2>&1
    fi
    if [[ "${data_before}" == "1" ]]; then
        "${ADB[@]}" shell svc data enable >/dev/null 2>&1
    else
        "${ADB[@]}" shell svc data disable >/dev/null 2>&1
    fi
}

read_report_run_as()
{
    "${ADB[@]}" exec-out run-as "${PACKAGE_NAME}" sh -c \
        "test -f '${REMOTE_RESULT}' && cat '${REMOTE_RESULT}'" \
        2>/dev/null | tr -d '\r' || true
}

read_report_shell()
{
    "${ADB[@]}" shell \
        "if test -f '${REMOTE_RESULT}'; then cat '${REMOTE_RESULT}'; fi" \
        2>/dev/null | tr -d '\r' || true
}

read_report()
{
    local value
    value="$(read_report_run_as)"
    if [[ -z "${value}" ]]; then
        value="$(read_report_shell)"
    fi
    printf '%s' "${value}"
}

capture_diagnostics()
{
    # The structured shipping acceptance report is sufficient for an
    # unprivileged, privacy-safe physical proof. Unrestricted device logs,
    # system/network inventories and screenshots may contain private data.
    read_report > "${REPORT_DIR}/device-report.json" || true
}

cleanup()
{
    local exit_code=$?
    trap - EXIT
    capture_diagnostics
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
    restore_network
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10 \
        >/dev/null 2>&1 || true
    exit "${exit_code}"
}
trap cleanup EXIT

"${ADB[@]}" install -r -g "${APK_PATH}" > "${REPORT_DIR}/install.txt"
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/pm-clear.txt"

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

"${ADB[@]}" shell svc wifi disable >/dev/null
"${ADB[@]}" shell svc data disable >/dev/null
sleep 2
# Only the two network-state flags relevant to this offline test are
# captured; never dump connectivity details for unrelated device apps.
{
    printf 'wifi_on=%s\n' "$("${ADB[@]}" shell settings get global wifi_on | tr -d '\r')"
    printf 'mobile_data=%s\n' "$("${ADB[@]}" shell settings get global mobile_data | tr -d '\r')"
} > "${REPORT_DIR}/network-disabled.txt"

"${ADB[@]}" shell mkdir -p "${REMOTE_FILES_DIR}"
"${ADB[@]}" shell rm -f "${REMOTE_RESULT}" "${REMOTE_RESULT}.tmp"
"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
    > "${REPORT_DIR}/prepare.txt"
"${ADB[@]}" shell am start -W \
    -n "${LAUNCH_COMPONENT}" \
    -a android.intent.action.MAIN \
    -c android.intent.category.LAUNCHER \
    --ez reachy_rma190_core_e2e_acceptance true \
    > "${REPORT_DIR}/launch.txt"
cat "${REPORT_DIR}/launch.txt"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30 \
    > "${REPORT_DIR}/focus.txt"

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
report_json=""
while true; do
    report_json="$(read_report)"
    if [[ "${report_json}" == \{* ]]; then
        break
    fi
    report_json=""
    if (( $(date +%s) >= deadline )); then
        printf 'Timed out waiting for RMA-190 core end-to-end evidence.\n' >&2
        exit 1
    fi
    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf 'Unity exited before RMA-190 evidence was written.\n' >&2
        exit 1
    fi
    sleep "${POLL_SECONDS}"
done

printf '%s\n' "${report_json}" > "${REPORT_DIR}/${RESULT_FILE}"
python3 - "${REPORT_DIR}/${RESULT_FILE}" <<'PY'
from __future__ import annotations

import json
import re
import sys

report = json.load(open(sys.argv[1], encoding="utf-8"))
if report.get("status") != "passed":
    detail = " ".join(str(report.get("error", "")).split())
    detail = re.sub(r"https?://\S+", "[url-redacted]", detail)
    detail = re.sub(
        r"(?i)\b(?:authorization|bearer|token|secret|api[_-]?key)\b\s*[:=]\s*\S+",
        "[credential-redacted]",
        detail,
    )
    print(
        "RMA-190 core player failure: "
        f"status={report.get('status', 'missing')}; "
        f"error={detail[:512] or 'unspecified'}",
        file=sys.stderr,
    )
PY
if ! python3 - "${REPORT_DIR}/${RESULT_FILE}" <<'PY'
from __future__ import annotations

import json
import math
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
report = json.loads(path.read_text(encoding="utf-8"))
assert report["status"] == "passed", "player report status is not passed"
assert report["network_offline"] is True, report
assert report["active_network_validated"] is False, report
assert report["simulation_service"] == "ReachyProductionSimulationApplicationService", report
assert report["behavior_service"] == "ReachyBaselineBehaviorApplicationService", report
assert report["runtime_status"] == "Running", report
assert report["model_hash"].startswith("0x") and int(report["model_hash"], 16) != 0, report
assert report["body_count"] == 18, report
assert report["qpos_count"] == 37, report
assert report["qvel_count"] == 30, report
assert report["actuator_observation_count"] == 9, report
assert report["neutral_reset_succeeded"] is True, report
assert report["neutral_reset_continuity_advanced"] is True, report
assert report["gesture_kind"] == "Acknowledgment", report
assert len(report["gesture_plan_sha256"]) == 64, report
assert report["gesture_plan_frame_count"] > 0, report
assert report["gesture_first_submitted_frame_count"] == report["gesture_plan_frame_count"], report
assert report["gesture_second_submitted_frame_count"] == report["gesture_plan_frame_count"], report
assert report["gesture_actual_motion_observed"] is True, report
assert report["behavior_suspended_for_direct_replay"] is True, report
assert report["gesture_replay_within_tolerance"] is True, report
for field in (
    "gesture_first_maximum_motion_radians",
    "gesture_second_maximum_motion_radians",
    "gesture_replay_maximum_qpos_error",
    "gesture_replay_maximum_qvel_error",
    "gesture_replay_tolerance",
):
    assert math.isfinite(float(report[field])), (field, report)
assert report["human_asr_transcript_injected"] is False, report
assert report["cloud_credentials_injected"] is False, report
assert report["local_model_injected"] is False, report
print(json.dumps(report, indent=2, sort_keys=True))
PY
then
    printf '%s\n' 'RMA-190 core end-to-end report validation failed.' >&2
    exit 1
fi

capture_diagnostics
sha256sum "${APK_PATH}" > "${REPORT_DIR}/apk.sha256"
sha256sum "${REPORT_DIR}/${RESULT_FILE}" > "${REPORT_DIR}/report.sha256"
{
    printf 'device_serial=%s\n' "${DEVICE_SERIAL}"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
    printf 'wifi_before=%s\n' "${wifi_before}"
    printf 'mobile_data_before=%s\n' "${data_before}"
} > "${REPORT_DIR}/environment.txt"
printf 'RMA-190 offline launch, full-model reset, and deterministic gesture replay passed.\n'

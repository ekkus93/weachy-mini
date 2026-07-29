#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
STAGING_DIR="${MUJOCO_ANDROID_OUTPUT_DIR:-${ROOT_DIR}/Assets/Plugins/Android/libs/arm64-v8a}"
REMOTE_DIR="/data/local/tmp/weachy-mujoco-probe"
REMOTE_MODEL_DIR="${REMOTE_DIR}/reachy-model"
REPORT_DIR="${REACHY_PROBE_REPORT_DIR:-${ROOT_DIR}/diagnostics-output/mujoco-probe}"
STEP_COUNT="${REACHY_PROBE_STEP_COUNT:-900000}"
REACHY_MODEL_STEP_COUNT="${REACHY_MODEL_PROBE_STEP_COUNT:-100}"
ADB_BIN="${ADB:-adb}"
REQUESTED_SERIAL="${REACHY_ANDROID_SERIAL:-${ANDROID_SERIAL:-}}"
REACHY_MODEL_DIR="${STAGING_DIR}/reachy-model"
REACHY_MODEL_PATH="${REACHY_MODEL_DIR}/reachy_mini.xml"
REACHY_BASELINE_PATH="${REACHY_MODEL_DIR}/model-baseline.json"
REFERENCE_SCENARIO_PATH="${STAGING_DIR}/reference-scenario.json"
DESKTOP_TRACE_PATH="${STAGING_DIR}/REFERENCE_TRACE_DESKTOP.json"
TRACE_LOCK_PATH="${STAGING_DIR}/reference-trace-desktop.lock.json"
DEVICE_SERIAL=""

dump_failure_diagnostics()
{
    local status=$?
    if (( status == 0 )); then
        return
    fi

    trap - EXIT
    set +e
    printf 'Android MuJoCo probe failed with exit status %s.\n' "${status}" >&2
    if [[ -n "${DEVICE_SERIAL}" ]]; then
        printf '%s\n' 'Remote probe files:' >&2
        "${ADB_BIN}" -s "${DEVICE_SERIAL}" shell \
            "find '${REMOTE_DIR}' -maxdepth 4 -type f -print 2>/dev/null | sort" >&2
    fi
    if [[ -d "${REPORT_DIR}" ]]; then
        printf '%s\n' 'Partial local reports:' >&2
        while IFS= read -r report; do
            printf '%s\n' "--- ${report}" >&2
            cat "${report}" >&2
        done < <(find "${REPORT_DIR}" -maxdepth 1 -type f -print | sort)
    fi
    exit "${status}"
}
trap dump_failure_diagnostics EXIT

for required_file in \
    libmujoco.so \
    reachy_mujoco_probe_runner \
    reachy_mujoco_reference_runner \
    closed_loop_probe.xml \
    malformed_probe.xml \
    reference-scenario.json \
    REFERENCE_TRACE_DESKTOP.json \
    reference-trace-desktop.lock.json; do
    if [[ ! -f "${STAGING_DIR}/${required_file}" ]]; then
        printf 'Missing staged probe file: %s\n' "${STAGING_DIR}/${required_file}" >&2
        exit 1
    fi
done

for required_file in \
    "${REACHY_MODEL_PATH}" \
    "${REACHY_MODEL_DIR}/MODEL_MAP.json" \
    "${REACHY_BASELINE_PATH}"; do
    if [[ ! -f "${required_file}" ]]; then
        printf 'Missing staged Reachy model file: %s\n' "${required_file}" >&2
        exit 1
    fi
done

command -v "${ADB_BIN}" >/dev/null
python3 "${SCRIPT_DIR}/validate_reference_trace_lock.py" \
    --scenario "${REFERENCE_SCENARIO_PATH}" \
    --lock "${TRACE_LOCK_PATH}" \
    --trace "${DESKTOP_TRACE_PATH}"

select_device_serial()
{
    if [[ -n "${REQUESTED_SERIAL}" ]]; then
        if ! "${ADB_BIN}" -s "${REQUESTED_SERIAL}" get-state 2>/dev/null \
            | grep -Fx 'device' >/dev/null; then
            printf 'Requested Android device is not online: %s\n' \
                "${REQUESTED_SERIAL}" >&2
            "${ADB_BIN}" devices -l >&2
            exit 1
        fi
        printf '%s\n' "${REQUESTED_SERIAL}"
        return
    fi

    local serial
    local abi
    local -a arm64_serials=()
    while IFS= read -r serial; do
        [[ -n "${serial}" ]] || continue
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi \
            | tr -d '\r')"
        if [[ "${abi}" == "arm64-v8a" ]]; then
            arm64_serials+=("${serial}")
        fi
    done < <(
        "${ADB_BIN}" devices \
            | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )

    if [[ "${#arm64_serials[@]}" -ne 1 ]]; then
        printf 'Exactly one online physical arm64-v8a Android device is required; found %s.\n' \
            "${#arm64_serials[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi

    printf '%s\n' "${arm64_serials[0]}"
}

validate_probe_report()
{
    local report_path="$1"
    local expected_steps="$2"
    python3 - "${report_path}" "${expected_steps}" <<'PY'
from __future__ import annotations

import json
import sys
from pathlib import Path

report_path = Path(sys.argv[1])
expected_steps = int(sys.argv[2])
report = json.loads(report_path.read_text(encoding="utf-8"))
if report.get("status") != "ok":
    raise SystemExit(f"probe failed: {report}")
if report.get("completed_steps") != expected_steps:
    raise SystemExit(f"probe completed unexpected step count: {report}")
if report.get("simulated_seconds", 0.0) < expected_steps * 0.002 - 0.01:
    raise SystemExit(f"probe simulated insufficient time: {report}")
if report.get("warning_count") != 0:
    raise SystemExit(f"probe produced MuJoCo warnings: {report}")
print(json.dumps(report, indent=2, sort_keys=True))
PY
}

validate_reachy_report()
{
    local report_path="$1"
    local expected_steps="$2"
    python3 - "${report_path}" "${REACHY_BASELINE_PATH}" "${expected_steps}" <<'PY'
from __future__ import annotations

import json
import sys
from pathlib import Path

report_path = Path(sys.argv[1])
baseline_path = Path(sys.argv[2])
expected_steps = int(sys.argv[3])
report = json.loads(report_path.read_text(encoding="utf-8"))
baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
if report.get("status") != "ok":
    raise SystemExit(f"full Reachy model probe failed: {report}")
if report.get("completed_steps") != expected_steps:
    raise SystemExit(f"full Reachy model completed unexpected steps: {report}")
if report.get("warning_count") != 0:
    raise SystemExit(f"full Reachy model produced MuJoCo warnings: {report}")
expected_counts = baseline["compiled_counts"]
actual_counts = report.get("compiled_counts")
if actual_counts != expected_counts:
    raise SystemExit(
        "Android compiled counts differ from desktop baseline: "
        f"expected {expected_counts}, found {actual_counts}"
    )
print(json.dumps(report, indent=2, sort_keys=True))
PY
}

DEVICE_SERIAL="$(select_device_serial)"
ADB_COMMAND=("${ADB_BIN}" -s "${DEVICE_SERIAL}")

mkdir -p "${REPORT_DIR}"
TIMESTAMP="$(date -u +'%Y%m%dT%H%M%SZ')"
CONSTRAINED_REPORT_PATH="${REPORT_DIR}/${TIMESTAMP}-closed-loop.json"
REACHY_REPORT_PATH="${REPORT_DIR}/${TIMESTAMP}-reachy-model.json"
ANDROID_TRACE_PATH="${REPORT_DIR}/${TIMESTAMP}-reference-trace-android.json"
TRACE_COMPARISON_PATH="${REPORT_DIR}/${TIMESTAMP}-reference-comparison.json"
DEVICE_PATH="${REPORT_DIR}/${TIMESTAMP}-device.txt"

{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'manufacturer=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.product.model | tr -d '\r')"
    printf 'device=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.product.device | tr -d '\r')"
    printf 'android_release=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.build.version.release | tr -d '\r')"
    printf 'sdk=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
} > "${DEVICE_PATH}"

"${ADB_COMMAND[@]}" shell \
    "rm -rf '${REMOTE_DIR}' && mkdir -p '${REMOTE_MODEL_DIR}'"
"${ADB_COMMAND[@]}" push \
    "${STAGING_DIR}/libmujoco.so" \
    "${REMOTE_DIR}/libmujoco.so" >/dev/null
"${ADB_COMMAND[@]}" push \
    "${STAGING_DIR}/reachy_mujoco_probe_runner" \
    "${REMOTE_DIR}/reachy_mujoco_probe_runner" >/dev/null
"${ADB_COMMAND[@]}" push \
    "${STAGING_DIR}/reachy_mujoco_reference_runner" \
    "${REMOTE_DIR}/reachy_mujoco_reference_runner" >/dev/null
"${ADB_COMMAND[@]}" push \
    "${STAGING_DIR}/closed_loop_probe.xml" \
    "${REMOTE_DIR}/closed_loop_probe.xml" >/dev/null
"${ADB_COMMAND[@]}" push \
    "${STAGING_DIR}/malformed_probe.xml" \
    "${REMOTE_DIR}/malformed_probe.xml" >/dev/null
"${ADB_COMMAND[@]}" push \
    "${REACHY_MODEL_DIR}/." \
    "${REMOTE_MODEL_DIR}/" >/dev/null
"${ADB_COMMAND[@]}" shell \
    "chmod 700 '${REMOTE_DIR}/reachy_mujoco_probe_runner' '${REMOTE_DIR}/reachy_mujoco_reference_runner'"

for remote_required_file in \
    "${REMOTE_MODEL_DIR}/reachy_mini.xml" \
    "${REMOTE_MODEL_DIR}/MODEL_MAP.json" \
    "${REMOTE_MODEL_DIR}/model-baseline.json"; do
    if ! "${ADB_COMMAND[@]}" shell "test -f '${remote_required_file}'"; then
        printf 'Required remote model file is missing: %s\n' \
            "${remote_required_file}" >&2
        exit 1
    fi
done

MALFORMED_OUTPUT="$("${ADB_COMMAND[@]}" shell \
    "cd '${REMOTE_DIR}' && LD_LIBRARY_PATH='${REMOTE_DIR}' ./reachy_mujoco_probe_runner malformed_probe.xml 1" \
    2>&1 || true)"
if ! printf '%s' "${MALFORMED_OUTPUT}" | grep -F '"status":"model_load_failed"' >/dev/null; then
    printf 'Malformed model did not produce the expected structured error: %s\n' \
        "${MALFORMED_OUTPUT}" >&2
    exit 1
fi

"${ADB_COMMAND[@]}" shell \
    "cd '${REMOTE_DIR}' && LD_LIBRARY_PATH='${REMOTE_DIR}' ./reachy_mujoco_probe_runner closed_loop_probe.xml '${STEP_COUNT}'" \
    | tr -d '\r' > "${CONSTRAINED_REPORT_PATH}"
validate_probe_report "${CONSTRAINED_REPORT_PATH}" "${STEP_COUNT}"

"${ADB_COMMAND[@]}" shell \
    "cd '${REMOTE_DIR}' && LD_LIBRARY_PATH='${REMOTE_DIR}' ./reachy_mujoco_probe_runner reachy-model/reachy_mini.xml '${REACHY_MODEL_STEP_COUNT}'" \
    | tr -d '\r' > "${REACHY_REPORT_PATH}"
validate_reachy_report "${REACHY_REPORT_PATH}" "${REACHY_MODEL_STEP_COUNT}"

"${ADB_COMMAND[@]}" shell \
    "cd '${REMOTE_DIR}' && LD_LIBRARY_PATH='${REMOTE_DIR}' ./reachy_mujoco_reference_runner reachy-model/reachy_mini.xml android_arm64_api26" \
    | tr -d '\r' > "${ANDROID_TRACE_PATH}"
python3 "${SCRIPT_DIR}/compare_reachy_reference_trace.py" \
    --scenario "${REFERENCE_SCENARIO_PATH}" \
    --desktop "${DESKTOP_TRACE_PATH}" \
    --android "${ANDROID_TRACE_PATH}" \
    --output "${TRACE_COMPARISON_PATH}"

trap - EXIT
printf '%s\n' \
    "Constrained probe report: ${CONSTRAINED_REPORT_PATH}" \
    "Reachy model report: ${REACHY_REPORT_PATH}" \
    "Android reference trace: ${ANDROID_TRACE_PATH}" \
    "Reference comparison: ${TRACE_COMPARISON_PATH}" \
    "Device report: ${DEVICE_PATH}"

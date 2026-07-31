#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
ARTIFACT_DIR="${MUJOCO_ANDROID_OUTPUT_DIR:-${ROOT_DIR}/build/rma065-android-artifact}"
REPORT_DIR="${RMA065_ANDROID_REPORT_DIR:-${ROOT_DIR}/diagnostics-output/rma065-android}"
REMOTE_DIR="/data/local/tmp/weachy-rma065"
ADB_BIN="${ADB:-adb}"
REQUESTED_SERIAL="${REACHY_ANDROID_SERIAL:-${ANDROID_SERIAL:-}}"
DEVICE_SERIAL=""

select_device_serial()
{
    if [[ -n "${REQUESTED_SERIAL}" ]]; then
        if ! "${ADB_BIN}" -s "${REQUESTED_SERIAL}" get-state 2>/dev/null | grep -Fx device >/dev/null; then
            printf 'Requested Android device is not online: %s\n' "${REQUESTED_SERIAL}" >&2
            exit 1
        fi
        printf '%s\n' "${REQUESTED_SERIAL}"
        return
    fi
    local serial abi
    local -a devices=()
    while IFS= read -r serial; do
        [[ -n "${serial}" ]] || continue
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
        if [[ "${abi}" == arm64-v8a ]]; then
            devices+=("${serial}")
        fi
    done < <("${ADB_BIN}" devices | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}')
    if [[ "${#devices[@]}" -ne 1 ]]; then
        printf 'Exactly one physical arm64-v8a Android device is required; found %s.\n' "${#devices[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi
    printf '%s\n' "${devices[0]}"
}

for required in \
    libmujoco.so \
    reachy_mujoco_collision_benchmark_runner \
    source-model/reachy_mini.xml \
    enhanced-model/reachy_mini.xml \
    collision-hard-stop-baseline.json; do
    if [[ ! -s "${ARTIFACT_DIR}/${required}" ]]; then
        printf 'RMA-065 Android artifact is missing: %s\n' "${ARTIFACT_DIR}/${required}" >&2
        exit 1
    fi
done
command -v "${ADB_BIN}" >/dev/null
DEVICE_SERIAL="$(select_device_serial)"
ADB_COMMAND=("${ADB_BIN}" -s "${DEVICE_SERIAL}")
mkdir -p "${REPORT_DIR}"
REPORT_PATH="${REPORT_DIR}/rma065-android-collision-benchmark.json"
DEVICE_PATH="${REPORT_DIR}/rma065-android-device.txt"

{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'manufacturer=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.product.model | tr -d '\r')"
    printf 'android_release=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.build.version.release | tr -d '\r')"
    printf 'sdk=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB_COMMAND[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
} > "${DEVICE_PATH}"

"${ADB_COMMAND[@]}" shell "rm -rf '${REMOTE_DIR}' && mkdir -p '${REMOTE_DIR}'"
"${ADB_COMMAND[@]}" push "${ARTIFACT_DIR}/libmujoco.so" "${REMOTE_DIR}/libmujoco.so" >/dev/null
"${ADB_COMMAND[@]}" push \
    "${ARTIFACT_DIR}/reachy_mujoco_collision_benchmark_runner" \
    "${REMOTE_DIR}/reachy_mujoco_collision_benchmark_runner" >/dev/null
"${ADB_COMMAND[@]}" push "${ARTIFACT_DIR}/source-model" "${REMOTE_DIR}/source-model" >/dev/null
"${ADB_COMMAND[@]}" push "${ARTIFACT_DIR}/enhanced-model" "${REMOTE_DIR}/enhanced-model" >/dev/null
"${ADB_COMMAND[@]}" shell "chmod 700 '${REMOTE_DIR}/reachy_mujoco_collision_benchmark_runner'"

STEPS="$(python3 - "${ARTIFACT_DIR}/collision-hard-stop-baseline.json" <<'PY'
import json, sys
from pathlib import Path
print(json.loads(Path(sys.argv[1]).read_text())['android_budget']['benchmark_steps'])
PY
)"
"${ADB_COMMAND[@]}" shell \
    "cd '${REMOTE_DIR}' && LD_LIBRARY_PATH='${REMOTE_DIR}' ./reachy_mujoco_collision_benchmark_runner source-model/reachy_mini.xml enhanced-model/reachy_mini.xml '${STEPS}'" \
    | tr -d '\r' > "${REPORT_PATH}"

python3 - "${REPORT_PATH}" "${ARTIFACT_DIR}/collision-hard-stop-baseline.json" <<'PY'
import json, math, sys
from pathlib import Path
report = json.loads(Path(sys.argv[1]).read_text())
profile = json.loads(Path(sys.argv[2]).read_text())
budget = profile['android_budget']
if report.get('status') != 'ok':
    raise SystemExit(f'Android benchmark failed: {report}')
for label in ('source', 'enhanced'):
    result = report[label]
    if result['steps'] != budget['benchmark_steps']:
        raise SystemExit(f'{label} step count mismatch: {result}')
    if result['warning_count'] != 0:
        raise SystemExit(f'{label} produced MuJoCo warnings: {result}')
    if not math.isfinite(result['realtime_factor']) or result['realtime_factor'] < budget['minimum_realtime_factor']:
        raise SystemExit(f'{label} realtime factor is below budget: {result}')
if report['p95_step_overhead_ratio'] > budget['maximum_p95_step_overhead_ratio']:
    raise SystemExit(f"collision overhead exceeds budget: {report}")
if report['enhanced']['maximum_penetration_metres'] > profile['contact_parameters']['maximum_penetration_metres']:
    raise SystemExit(f"enhanced penetration exceeds budget: {report}")
print(json.dumps(report, indent=2, sort_keys=True))
PY

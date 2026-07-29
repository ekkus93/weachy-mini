#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
STAGING_DIR="${MUJOCO_ANDROID_OUTPUT_DIR:-${ROOT_DIR}/Assets/Plugins/Android/libs/arm64-v8a}"
REMOTE_DIR="/data/local/tmp/weachy-mujoco-probe"
REPORT_DIR="${REACHY_PROBE_REPORT_DIR:-${ROOT_DIR}/diagnostics-output/mujoco-probe}"
STEP_COUNT="${REACHY_PROBE_STEP_COUNT:-900000}"
ADB="${ADB:-adb}"

for required_file in \
    libmujoco.so \
    reachy_mujoco_probe_runner \
    closed_loop_probe.xml \
    malformed_probe.xml; do
    if [[ ! -f "${STAGING_DIR}/${required_file}" ]]; then
        printf 'Missing staged probe file: %s\n' "${STAGING_DIR}/${required_file}" >&2
        exit 1
    fi
done

command -v "${ADB}" >/dev/null
DEVICE_COUNT="$(${ADB} devices | awk 'NR > 1 && $2 == "device" {count += 1} END {print count + 0}')"
if [[ "${DEVICE_COUNT}" != "1" ]]; then
    printf 'Exactly one authorized Android device is required; found %s.\n' "${DEVICE_COUNT}" >&2
    exit 1
fi

mkdir -p "${REPORT_DIR}"
TIMESTAMP="$(date -u +'%Y%m%dT%H%M%SZ')"
REPORT_PATH="${REPORT_DIR}/${TIMESTAMP}.json"
DEVICE_PATH="${REPORT_DIR}/${TIMESTAMP}-device.txt"

{
    printf 'manufacturer=%s\n' "$(${ADB} shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' "$(${ADB} shell getprop ro.product.model | tr -d '\r')"
    printf 'device=%s\n' "$(${ADB} shell getprop ro.product.device | tr -d '\r')"
    printf 'android_release=%s\n' "$(${ADB} shell getprop ro.build.version.release | tr -d '\r')"
    printf 'sdk=%s\n' "$(${ADB} shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$(${ADB} shell getprop ro.product.cpu.abi | tr -d '\r')"
} > "${DEVICE_PATH}"

${ADB} shell "rm -rf '${REMOTE_DIR}' && mkdir -p '${REMOTE_DIR}'"
${ADB} push "${STAGING_DIR}/libmujoco.so" "${REMOTE_DIR}/libmujoco.so" >/dev/null
${ADB} push \
    "${STAGING_DIR}/reachy_mujoco_probe_runner" \
    "${REMOTE_DIR}/reachy_mujoco_probe_runner" >/dev/null
${ADB} push "${STAGING_DIR}/closed_loop_probe.xml" "${REMOTE_DIR}/closed_loop_probe.xml" >/dev/null
${ADB} push "${STAGING_DIR}/malformed_probe.xml" "${REMOTE_DIR}/malformed_probe.xml" >/dev/null
${ADB} shell "chmod 700 '${REMOTE_DIR}/reachy_mujoco_probe_runner'"

MALFORMED_OUTPUT="$(${ADB} shell \
    "cd '${REMOTE_DIR}' && LD_LIBRARY_PATH='${REMOTE_DIR}' ./reachy_mujoco_probe_runner malformed_probe.xml 1" \
    2>&1 || true)"
if ! printf '%s' "${MALFORMED_OUTPUT}" | grep -F '"status":"model_load_failed"' >/dev/null; then
    printf 'Malformed model did not produce the expected structured error: %s\n' \
        "${MALFORMED_OUTPUT}" >&2
    exit 1
fi

${ADB} shell \
    "cd '${REMOTE_DIR}' && LD_LIBRARY_PATH='${REMOTE_DIR}' ./reachy_mujoco_probe_runner closed_loop_probe.xml '${STEP_COUNT}'" \
    | tr -d '\r' > "${REPORT_PATH}"

python3 - "${REPORT_PATH}" "${STEP_COUNT}" <<'PY'
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
print(json.dumps(report, indent=2, sort_keys=True))
PY

printf 'Probe report: %s\nDevice report: %s\n' "${REPORT_PATH}" "${DEVICE_PATH}"

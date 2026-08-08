#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA134_REPORT_DIR:-${ROOT_DIR}/build/rma134-local-llm-report}"
CONFIG_PATH="${ROOT_DIR}/benchmarks/rma133/candidates-v6.json"
PACKAGE_NAME="com.ekkus.weachymini"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
RESULT_FILE="rma134-local-llm-provider-state.json"
INPUT_MODEL_FILE="rma134-selected-model-input.gguf"
MANAGED_STORE_DIR="rma134-managed-models"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT="${REMOTE_FILES_DIR}/${RESULT_FILE}"
REMOTE_MODEL="${REMOTE_FILES_DIR}/${INPUT_MODEL_FILE}"
REMOTE_STORE="${REMOTE_FILES_DIR}/${MANAGED_STORE_DIR}"
TIMEOUT_SECONDS="${RMA134_TIMEOUT_SECONDS:-900}"
POLL_SECONDS="${RMA134_POLL_SECONDS:-1}"
CACHE_DIR="${RMA134_MODEL_CACHE_DIR:-${HOME}/.cache/weachy-mini/rma133/models}"

for path in "${APK_PATH}" "${CONFIG_PATH}" "${FOREGROUND_HELPER}"; do
    if [[ ! -s "${path}" ]]; then
        printf 'RMA-134 required file is missing: %s\n' "${path}" >&2
        exit 1
    fi
done
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'RMA-134 timeout must be a positive integer: %s\n' "${TIMEOUT_SECONDS}" >&2
    exit 1
fi
for tool in "${ADB_BIN}" python3 curl sha256sum; do
    command -v "${tool}" >/dev/null
 done

mapfile -t model_fields < <(
    python3 - "${CONFIG_PATH}" <<'PY'
import json
import sys
from pathlib import Path

config = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
selected = next(
    candidate
    for candidate in config["candidates"]
    if candidate["candidate_id"] == "qwen3-0.6b-q4-k-m"
)
artifact = selected["artifact"]
for value in (
    artifact["url"],
    artifact["filename"],
    artifact["file_size_bytes"],
    artifact["sha256"],
):
    print(value)
PY
)
if (( ${#model_fields[@]} != 4 )); then
    printf '%s\n' 'Could not read the exact RMA-133 selected artifact contract.' >&2
    exit 1
fi
MODEL_URL="${model_fields[0]}"
MODEL_FILENAME="${model_fields[1]}"
MODEL_SIZE="${model_fields[2]}"
MODEL_SHA="${model_fields[3]}"
mkdir -p "${CACHE_DIR}"
MODEL_PATH="${CACHE_DIR}/${MODEL_SHA}-${MODEL_FILENAME}"

verify_model()
{
    [[ -s "${MODEL_PATH}" ]] || return 1
    [[ "$(stat -c '%s' "${MODEL_PATH}")" == "${MODEL_SIZE}" ]] || return 1
    [[ "$(sha256sum "${MODEL_PATH}" | awk '{print $1}')" == "${MODEL_SHA}" ]]
}

if ! verify_model; then
    rm -f -- "${MODEL_PATH}"
    partial="${MODEL_PATH}.partial.$$"
    rm -f -- "${partial}"
    curl \
        --fail-with-body \
        --location \
        --proto '=https' \
        --tlsv1.2 \
        --retry 2 \
        --retry-all-errors \
        --output "${partial}" \
        "${MODEL_URL}"
    if [[ "$(stat -c '%s' "${partial}")" != "${MODEL_SIZE}" ]] ||
       [[ "$(sha256sum "${partial}" | awk '{print $1}')" != "${MODEL_SHA}" ]]; then
        rm -f -- "${partial}"
        printf '%s\n' 'RMA-134 selected model cache failed exact size/SHA verification.' >&2
        exit 1
    fi
    mv -- "${partial}" "${MODEL_PATH}"
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
        local abi sdk qemu
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
        sdk="$("${ADB_BIN}" -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\r')"
        qemu="$("${ADB_BIN}" -s "${serial}" shell getprop ro.kernel.qemu | tr -d '\r')"
        if [[ "${abi}" == "arm64-v8a" && "${sdk}" =~ ^[0-9]+$ ]] &&
           (( sdk >= 26 )) && [[ "${qemu}" != "1" ]]; then
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
    "${ADB[@]}" shell "ls -la '${REMOTE_FILES_DIR}' 2>&1" \
        > "${REPORT_DIR}/external-files.txt"
    "${ADB[@]}" exec-out screencap -p > "${REPORT_DIR}/device-screen-final.png"
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
    "${ADB[@]}" shell rm -f "${REMOTE_MODEL}" "${REMOTE_RESULT}.tmp" >/dev/null 2>&1
    "${ADB[@]}" shell rm -rf "${REMOTE_STORE}" >/dev/null 2>&1
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10 >/dev/null 2>&1
    exit "${exit_code}"
}
trap cleanup EXIT

"${ADB[@]}" install -r -g "${APK_PATH}" > "${REPORT_DIR}/install.txt"
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
"${ADB[@]}" shell rm -f "${REMOTE_RESULT}" "${REMOTE_RESULT}.tmp" "${REMOTE_MODEL}"
"${ADB[@]}" shell rm -rf "${REMOTE_STORE}"
free_kib="$("${ADB[@]}" shell df -Pk "${REMOTE_FILES_DIR}" | tr -d '\r' | tail -n 1 | awk '{print $4}')"
required_kib=$(( (MODEL_SIZE + 1023) * 2 / 1024 + 262144 ))
if [[ ! "${free_kib}" =~ ^[0-9]+$ ]] || (( free_kib < required_kib )); then
    printf 'Insufficient device storage for RMA-134 import: free_kib=%s required_kib=%s\n' \
        "${free_kib}" "${required_kib}" >&2
    exit 1
fi
"${ADB[@]}" push "${MODEL_PATH}" "${REMOTE_MODEL}" > "${REPORT_DIR}/model-push.txt"
remote_size="$("${ADB[@]}" shell stat -c '%s' "${REMOTE_MODEL}" | tr -d '\r')"
remote_sha="$("${ADB[@]}" shell toybox sha256sum "${REMOTE_MODEL}" | tr -d '\r' | awk '{print $1}')"
if [[ "${remote_size}" != "${MODEL_SIZE}" || "${remote_sha}" != "${MODEL_SHA}" ]]; then
    printf 'Remote RMA-134 model integrity mismatch: size=%s sha=%s\n' \
        "${remote_size}" "${remote_sha}" >&2
    exit 1
fi

"${ADB[@]}" logcat -c
"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 > "${REPORT_DIR}/prepare.txt"
"${ADB[@]}" shell am start -W \
    -n "${LAUNCH_COMPONENT}" \
    -a android.intent.action.MAIN \
    -c android.intent.category.LAUNCHER \
    --ez reachy_rma134_acceptance true \
    > "${REPORT_DIR}/launch.txt"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30 > "${REPORT_DIR}/focus.txt"

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
report_json=""
while true; do
    report_json="$(
        "${ADB[@]}" shell \
            "if test -f '${REMOTE_RESULT}'; then cat '${REMOTE_RESULT}'; fi" \
            2>/dev/null | tr -d '\r'
    )"
    if [[ -n "${report_json}" ]]; then
        break
    fi
    if (( $(date +%s) >= deadline )); then
        printf '%s\n' 'Timed out waiting for RMA-134 provider evidence.' >&2
        exit 1
    fi
    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf '%s\n' 'Unity application exited before RMA-134 evidence was written.' >&2
        exit 1
    fi
    sleep "${POLL_SECONDS}"
done

printf '%s\n' "${report_json}" > "${REPORT_DIR}/${RESULT_FILE}"
python3 - "${REPORT_DIR}/${RESULT_FILE}" "${MODEL_SIZE}" "${MODEL_SHA}" <<'PY'
from __future__ import annotations

import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
expected_size = int(sys.argv[2])
expected_sha = sys.argv[3]
report = json.loads(path.read_text(encoding="utf-8"))
if report.get("status") != "passed":
    raise SystemExit(f"RMA-134 acceptance did not pass: {report}")
required_true = (
    "acceptance_enabled",
    "constrained_only",
    "cancellation_observed",
    "reset_succeeded",
    "reuse_completed",
    "simulation_remained_running",
)
if any(report.get(field) is not True for field in required_true):
    raise SystemExit(f"RMA-134 required truth field failed: {report}")
if int(report.get("runtime_abi_version", 0)) != 2:
    raise SystemExit(f"RMA-134 runtime ABI mismatch: {report}")
if report.get("manifest_id") != "rma133.qwen3-0.6b-q4-k-m.v1":
    raise SystemExit(f"RMA-134 manifest identity mismatch: {report}")
if report.get("model_id") != "qwen3-0.6b":
    raise SystemExit(f"RMA-134 selected model mismatch: {report}")
if int(report.get("model_file_size_bytes", 0)) != expected_size:
    raise SystemExit(f"RMA-134 selected artifact size mismatch: {report}")
if report.get("model_sha256") != expected_sha:
    raise SystemExit(f"RMA-134 selected artifact SHA mismatch: {report}")
if report.get("provider_requires_network") is not False:
    raise SystemExit(f"RMA-134 provider unexpectedly requires network: {report}")
if int(report.get("first_delta_count", 0)) <= 0:
    raise SystemExit(f"RMA-134 first generation did not stream: {report}")
if int(report.get("cancellation_delta_count", 0)) <= 0:
    raise SystemExit(f"RMA-134 cancellation was not exercised after streaming: {report}")
if int(report.get("reuse_delta_count", 0)) <= 0:
    raise SystemExit(f"RMA-134 post-reset generation did not stream: {report}")
if not str(report.get("first_speech", "")) or not str(report.get("reuse_speech", "")):
    raise SystemExit(f"RMA-134 validated speech is empty: {report}")
if int(report.get("simulation_steps_after", 0)) <= int(report.get("simulation_steps_before", 0)):
    raise SystemExit(f"RMA-134 authoritative simulation did not advance: {report}")
if int(report.get("simulation_step_delta", 0)) <= 0:
    raise SystemExit(f"RMA-134 simulation step delta is invalid: {report}")
PY

capture_diagnostics
sha256sum "${APK_PATH}" > "${REPORT_DIR}/apk.sha256"
sha256sum "${MODEL_PATH}" > "${REPORT_DIR}/selected-model.sha256"
sha256sum "${REPORT_DIR}/${RESULT_FILE}" > "${REPORT_DIR}/report.sha256"
{
    printf 'device_serial=%s\n' "${DEVICE_SERIAL}"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
    printf 'model=%s\n' "$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
    printf 'selected_model_sha256=%s\n' "${MODEL_SHA}"
    printf 'selected_model_size=%s\n' "${MODEL_SIZE}"
} > "${REPORT_DIR}/environment.txt"
printf '%s\n' 'RMA-134 physical local LLM provider acceptance passed.'

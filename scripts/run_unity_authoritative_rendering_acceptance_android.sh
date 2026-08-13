#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
PACKAGE_NAME="com.ekkus.weachymini"
REPORT_DIR="${UNITY_AUTHORITATIVE_REPORT_DIR:-${ROOT_DIR}/build/unity-authoritative-device-report}"
LAUNCH_READY_FILE="${REPORT_DIR}/launch-issued"
INSTALL_TIMEOUT_SECONDS="${UNITY_AUTHORITATIVE_INSTALL_TIMEOUT_SECONDS:-180}"
LAUNCH_READY_TIMEOUT_SECONDS="${UNITY_AUTHORITATIVE_LAUNCH_READY_TIMEOUT_SECONDS:-240}"
FOCUS_TIMEOUT_SECONDS="${UNITY_AUTHORITATIVE_FOCUS_TIMEOUT_SECONDS:-60}"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
IMPLEMENTATION="${ROOT_DIR}/scripts/run_unity_authoritative_rendering_acceptance_android_impl.sh"
RMA161_SCRIPT="${ROOT_DIR}/scripts/run_rma161_credential_acceptance_android.sh"
RMA161_REPORT_DIR="${REPORT_DIR}/rma161-credential-report"

if [[ ! -s "${FOREGROUND_HELPER}" ]]; then
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
fi
if [[ ! -s "${IMPLEMENTATION}" ]]; then
    printf 'Authoritative acceptance implementation is missing: %s\n' "${IMPLEMENTATION}" >&2
    exit 1
fi
if [[ ! -s "${RMA161_SCRIPT}" ]]; then
    printf 'RMA-161 credential acceptance script is missing: %s\n' "${RMA161_SCRIPT}" >&2
    exit 1
fi
if [[ ! "${INSTALL_TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( INSTALL_TIMEOUT_SECONDS <= 0 )); then
    printf 'Authoritative install timeout must be a positive integer: %s\n' \
        "${INSTALL_TIMEOUT_SECONDS}" >&2
    exit 1
fi
if [[ ! "${LAUNCH_READY_TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] ||
        (( LAUNCH_READY_TIMEOUT_SECONDS <= INSTALL_TIMEOUT_SECONDS + 20 )); then
    printf 'Launch-readiness timeout must exceed install timeout by more than 20 seconds: install=%s launch=%s\n' \
        "${INSTALL_TIMEOUT_SECONDS}" "${LAUNCH_READY_TIMEOUT_SECONDS}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null

select_device_serial()
{
    mapfile -t physical_serials < <(
        "${ADB_BIN}" devices \
            | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )
    local -a matching_serials=()
    local serial
    for serial in "${physical_serials[@]}"; do
        local abi
        local sdk
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
        sdk="$("${ADB_BIN}" -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\r')"
        if [[ "${abi}" == "arm64-v8a" && "${sdk}" =~ ^[0-9]+$ ]] && (( sdk >= 26 )); then
            matching_serials+=("${serial}")
        fi
    done
    if (( ${#matching_serials[@]} != 1 )); then
        printf 'Expected one physical arm64-v8a API-26+ device; found %s.\n' \
            "${#matching_serials[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi
    printf '%s\n' "${matching_serials[0]}"
}

DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:-$(select_device_serial)}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")
implementation_pid=""

restore_device()
{
    set +e
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10
}

on_exit()
{
    local exit_code=$?
    trap - EXIT
    set +e
    if [[ -n "${implementation_pid}" ]] && \
            kill -0 "${implementation_pid}" >/dev/null 2>&1; then
        kill "${implementation_pid}" >/dev/null 2>&1
        wait "${implementation_pid}" >/dev/null 2>&1
    fi
    restore_device
    exit "${exit_code}"
}
trap on_exit EXIT

ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20
"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
rm -f -- "${LAUNCH_READY_FILE}" "${LAUNCH_READY_FILE}.tmp"

REACHY_ANDROID_SERIAL="${DEVICE_SERIAL}" \
ADB_BIN="${ADB_BIN}" \
UNITY_AUTHORITATIVE_REPORT_DIR="${REPORT_DIR}" \
    bash "${IMPLEMENTATION}" &
implementation_pid=$!

launch_ready_deadline=$(( $(date +%s) + LAUNCH_READY_TIMEOUT_SECONDS ))
while [[ ! -s "${LAUNCH_READY_FILE}" ]]; do
    if ! kill -0 "${implementation_pid}" >/dev/null 2>&1; then
        if [[ -s "${LAUNCH_READY_FILE}" ]]; then
            break
        fi
        set +e
        wait "${implementation_pid}"
        implementation_status=$?
        set -e
        if (( implementation_status == 0 )); then
            implementation_status=1
        fi
        printf 'Authoritative acceptance exited before launch readiness with status %s.\n' \
            "${implementation_status}" >&2
        exit "${implementation_status}"
    fi

    if (( $(date +%s) >= launch_ready_deadline )); then
        kill "${implementation_pid}" >/dev/null 2>&1 || true
        wait "${implementation_pid}" >/dev/null 2>&1 || true
        printf 'Authoritative acceptance did not issue a verified launch within %s seconds.\n' \
            "${LAUNCH_READY_TIMEOUT_SECONDS}" >&2
        exit 1
    fi
    sleep 1
done

set +e
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" "${FOCUS_TIMEOUT_SECONDS}"
focus_status=$?
set -e
if (( focus_status != 0 )); then
    kill "${implementation_pid}" >/dev/null 2>&1 || true
    wait "${implementation_pid}" >/dev/null 2>&1 || true
    printf '%s\n' 'Authoritative acceptance did not acquire the foreground window.' >&2
    exit 1
fi

wait "${implementation_pid}"
implementation_pid=""

REACHY_ANDROID_SERIAL="${DEVICE_SERIAL}" \
ADB_BIN="${ADB_BIN}" \
RMA161_CREDENTIAL_REPORT_DIR="${RMA161_REPORT_DIR}" \
    bash "${RMA161_SCRIPT}"

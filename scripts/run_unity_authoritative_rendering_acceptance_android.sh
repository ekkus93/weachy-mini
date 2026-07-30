#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
PACKAGE_NAME="com.ekkus.weachymini"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
IMPLEMENTATION="${ROOT_DIR}/scripts/run_unity_authoritative_rendering_acceptance_android_impl.sh"

if [[ ! -s "${FOREGROUND_HELPER}" ]]; then
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
fi
if [[ ! -s "${IMPLEMENTATION}" ]]; then
    printf 'Authoritative acceptance implementation is missing: %s\n' "${IMPLEMENTATION}" >&2
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
    restore_device
    exit "${exit_code}"
}
trap on_exit EXIT

ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20
"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true

REACHY_ANDROID_SERIAL="${DEVICE_SERIAL}" \
ADB_BIN="${ADB_BIN}" \
    bash "${IMPLEMENTATION}" &
implementation_pid=$!

set +e
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 60
focus_status=$?
set -e
if (( focus_status != 0 )); then
    kill "${implementation_pid}" >/dev/null 2>&1 || true
    wait "${implementation_pid}" >/dev/null 2>&1 || true
    printf '%s\n' 'Authoritative acceptance did not acquire the foreground window.' >&2
    exit 1
fi

wait "${implementation_pid}"

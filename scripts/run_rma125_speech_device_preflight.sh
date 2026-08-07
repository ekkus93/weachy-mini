#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
REPORT_DIR="${RMA125_SPEECH_PREFLIGHT_REPORT_DIR:-${ROOT_DIR}/build/rma125-speech-device-preflight}"
MIN_SDK=31
REQUIRED_ABI="arm64-v8a"

command -v "${ADB_BIN}" >/dev/null
command -v python3 >/dev/null
command -v sha256sum >/dev/null

select_device_serial()
{
    mapfile -t serials < <(
        "${ADB_BIN}" devices \
            | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )

    if (( ${#serials[@]} != 1 )); then
        printf 'Expected exactly one connected physical Android device; found %s.\n' \
            "${#serials[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi

    printf '%s\n' "${serials[0]}"
}

DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:-$(select_device_serial)}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")

if [[ "$("${ADB[@]}" get-state 2>/dev/null || true)" != "device" ]]; then
    printf 'Selected Android device is not in adb device state.\n' >&2
    exit 1
fi

get_prop()
{
    "${ADB[@]}" shell getprop "$1" | tr -d '\r'
}

SDK="$(get_prop ro.build.version.sdk)"
ABI="$(get_prop ro.product.cpu.abi)"
MANUFACTURER="$(get_prop ro.product.manufacturer)"
MODEL="$(get_prop ro.product.model)"
ANDROID_RELEASE="$(get_prop ro.build.version.release)"
QEMU="$(get_prop ro.kernel.qemu)"
SERIAL_SHA256="$(printf '%s' "${DEVICE_SERIAL}" | sha256sum | awk '{print $1}')"

if [[ ! "${SDK}" =~ ^[0-9]+$ ]]; then
    printf 'Device reported a non-numeric Android SDK level: %s\n' "${SDK}" >&2
    exit 1
fi

ELIGIBILITY="eligible"
REASON="physical ARM64 API-31+ platform floor satisfied"
EXIT_CODE=0

if [[ "${QEMU}" == "1" ]]; then
    ELIGIBILITY="blocked"
    REASON="RMA-125 positive offline speech acceptance requires a physical device, not an emulator"
    EXIT_CODE=2
elif [[ "${ABI}" != "${REQUIRED_ABI}" ]]; then
    ELIGIBILITY="blocked"
    REASON="RMA-125 physical acceptance requires ARM64 (${REQUIRED_ABI}); device reports ${ABI}"
    EXIT_CODE=2
elif (( SDK < MIN_SDK )); then
    ELIGIBILITY="blocked"
    REASON="RMA-121 explicit on-device ASR requires Android API ${MIN_SDK}+; device reports API ${SDK}"
    EXIT_CODE=2
fi

rm -rf -- "${REPORT_DIR}"
mkdir -p "${REPORT_DIR}"
REPORT_PATH="${REPORT_DIR}/device-preflight.json"

export RMA125_PREFLIGHT_ELIGIBILITY="${ELIGIBILITY}"
export RMA125_PREFLIGHT_REASON="${REASON}"
export RMA125_PREFLIGHT_SDK="${SDK}"
export RMA125_PREFLIGHT_ABI="${ABI}"
export RMA125_PREFLIGHT_MANUFACTURER="${MANUFACTURER}"
export RMA125_PREFLIGHT_MODEL="${MODEL}"
export RMA125_PREFLIGHT_ANDROID_RELEASE="${ANDROID_RELEASE}"
export RMA125_PREFLIGHT_SERIAL_SHA256="${SERIAL_SHA256}"
export RMA125_PREFLIGHT_MIN_SDK="${MIN_SDK}"
export RMA125_PREFLIGHT_REQUIRED_ABI="${REQUIRED_ABI}"

python3 - "${REPORT_PATH}" <<'PY'
from __future__ import annotations

import json
import os
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
report = {
    "schema_version": 1,
    "rma": "RMA-125",
    "eligibility": os.environ["RMA125_PREFLIGHT_ELIGIBILITY"],
    "reason": os.environ["RMA125_PREFLIGHT_REASON"],
    "device": {
        "manufacturer": os.environ["RMA125_PREFLIGHT_MANUFACTURER"],
        "model": os.environ["RMA125_PREFLIGHT_MODEL"],
        "android_release": os.environ["RMA125_PREFLIGHT_ANDROID_RELEASE"],
        "sdk": int(os.environ["RMA125_PREFLIGHT_SDK"]),
        "abi": os.environ["RMA125_PREFLIGHT_ABI"],
        "serial_sha256": os.environ["RMA125_PREFLIGHT_SERIAL_SHA256"],
        "physical_device_required": True,
    },
    "requirements": {
        "minimum_sdk": int(os.environ["RMA125_PREFLIGHT_MIN_SDK"]),
        "required_abi": os.environ["RMA125_PREFLIGHT_REQUIRED_ABI"],
        "explicit_on_device_asr_runtime_probe_still_required": True,
        "exact_locale_offline_tts_probe_still_required": True,
        "network_disabled_physical_acceptance_still_required": True,
    },
    "claims": {
        "proves_on_device_asr_service_available": False,
        "proves_language_model_installed": False,
        "proves_offline_tts_voice_installed": False,
        "proves_network_disabled": False,
        "proves_rma125_complete": False,
    },
}
path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
PY

sha256sum "${REPORT_PATH}" > "${REPORT_DIR}/device-preflight.sha256"
printf 'RMA-125 device preflight: %s\n' "${ELIGIBILITY}"
printf 'Device: %s %s, Android %s/API %s, ABI %s\n' \
    "${MANUFACTURER}" "${MODEL}" "${ANDROID_RELEASE}" "${SDK}" "${ABI}"
printf '%s\n' "${REASON}"
printf 'Evidence: %s\n' "${REPORT_PATH}"

if (( EXIT_CODE != 0 )); then
    printf 'Do not substitute RMA-122/system ASR or a network-capable provider.\n' >&2
    exit "${EXIT_CODE}"
fi

printf 'Platform preflight passed. This does not prove the on-device ASR service, language model, offline TTS voice, network-disabled state, or end-to-end RMA-125 acceptance.\n'

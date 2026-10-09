#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA190_OFFLINE_SPEECH_REPORT_DIR:-${ROOT_DIR}/build/rma190-offline-speech-end-to-end-report}"
PACKAGE_NAME="com.ekkus.weachymini"
RESULT_FILE="rma190-offline-speech-end-to-end-acceptance.json"
MODEL_FILE="rma190-qwen3-0.6b-q4_k_m.gguf"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT="${REMOTE_FILES_DIR}/${RESULT_FILE}"
REMOTE_MODEL="${REMOTE_FILES_DIR}/${MODEL_FILE}"
MODEL_CACHE_ROOT="${RMA133_MODEL_CACHE_ROOT:-${HOME}/.cache/weachy-mini/rma133/models}"
TIMEOUT_SECONDS="${RMA190_OFFLINE_SPEECH_TIMEOUT_SECONDS:-360}"
POLL_SECONDS="${RMA190_OFFLINE_SPEECH_POLL_SECONDS:-0.5}"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"

command -v "${ADB_BIN}" >/dev/null
command -v python3 >/dev/null
command -v curl >/dev/null
command -v sha256sum >/dev/null
[[ -s "${APK_PATH}" ]] || {
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
}
[[ -s "${FOREGROUND_HELPER}" ]] || {
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
}

mkdir -p "${REPORT_DIR}" "${MODEL_CACHE_ROOT}"

mapfile -t MODEL_FIELDS < <(
    python3 - "${ROOT_DIR}/benchmarks/rma133/candidates-v6.json" <<'PY'
import json
import sys
from pathlib import Path

config = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
items = [
    item
    for item in config["candidates"]
    if item.get("candidate_id") == "qwen3-0.6b-q4-k-m"
]
if len(items) != 1:
    raise SystemExit(f"expected one qwen3 candidate, found {len(items)}")
candidate = items[0]
artifact = candidate["artifact"]
for value in (
    candidate["model_id"],
    artifact["filename"],
    artifact["url"],
    artifact["file_size_bytes"],
    artifact["sha256"],
    candidate.get("user_prompt_suffix", ""),
):
    print(value)
PY
)
(( ${#MODEL_FIELDS[@]} == 6 )) || {
    printf '%s\n' 'Could not resolve selected RMA-133 model.' >&2
    exit 1
}
MODEL_ID="${MODEL_FIELDS[0]}"
MODEL_FILENAME="${MODEL_FIELDS[1]}"
MODEL_URL="${MODEL_FIELDS[2]}"
MODEL_BYTES="${MODEL_FIELDS[3]}"
MODEL_SHA256="${MODEL_FIELDS[4]}"
MODEL_SUFFIX="${MODEL_FIELDS[5]}"

[[ "${MODEL_ID}" == "qwen3-0.6b" &&
   "${MODEL_BYTES}" == "396704416" &&
   "${MODEL_SHA256}" == "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e" &&
   "${MODEL_SUFFIX}" == "/no_think" ]] || {
    printf '%s\n' 'Frozen RMA-133 selected model contract drifted.' >&2
    exit 1
}

MODEL_CACHE_PATH="${MODEL_CACHE_ROOT}/${MODEL_SHA256}-${MODEL_FILENAME}"
verify_model()
{
    [[ -f "$1" ]] &&
        [[ "$(stat -c '%s' "$1")" == "${MODEL_BYTES}" ]] &&
        [[ "$(sha256sum "$1" | awk '{print $1}')" == "${MODEL_SHA256}" ]]
}

if [[ -e "${MODEL_CACHE_PATH}" ]] && ! verify_model "${MODEL_CACHE_PATH}"; then
    printf 'Cached selected model is invalid: %s\n' "${MODEL_CACHE_PATH}" >&2
    exit 1
fi
if [[ ! -e "${MODEL_CACHE_PATH}" ]]; then
    temporary="${MODEL_CACHE_PATH}.partial.$$"
    rm -f -- "${temporary}"
    curl --fail --location --silent --show-error         --output "${temporary}" "${MODEL_URL}"
    verify_model "${temporary}" || {
        rm -f -- "${temporary}"
        printf '%s\n' 'Downloaded selected model failed exact validation.' >&2
        exit 1
    }
    mv -f -- "${temporary}" "${MODEL_CACHE_PATH}"
fi
verify_model "${MODEL_CACHE_PATH}" || {
    printf '%s\n' 'Selected model cache preparation failed.' >&2
    exit 1
}

select_device_serial()
{
    mapfile -t serials < <(
        "${ADB_BIN}" devices |
            awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )
    local -a accepted=()
    local serial
    for serial in "${serials[@]}"; do
        local abi sdk
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
        sdk="$("${ADB_BIN}" -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\r')"
        if [[ "${abi}" == "arm64-v8a" &&
              "${sdk}" =~ ^[0-9]+$ ]] &&
            (( sdk >= 31 )); then
            accepted+=("${serial}")
        fi
    done
    if (( ${#accepted[@]} != 1 )); then
        printf 'Expected exactly one physical arm64-v8a API-31+ device; found %s.\n'             "${#accepted[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi
    printf '%s\n' "${accepted[0]}"
}

DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:-$(select_device_serial)}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")
rm -rf -- "${REPORT_DIR}"
mkdir -p "${REPORT_DIR}"

read_report()
{
    "${ADB[@]}" shell         "if test -f '${REMOTE_RESULT}'; then cat '${REMOTE_RESULT}'; fi"         2>/dev/null | tr -d '\r' || true
}

capture_diagnostics()
{
    # Never persist unrestricted logcat, microphone/audio dumps, or system
    # connectivity inventories. A real human utterance can appear in those
    # outputs even when the app's structured report intentionally omits it.
    # The shipping acceptance report contains only typed state and safe
    # exception-class diagnostics; the separate network-disabled.txt and
    # environment.txt already retain the relevant device/network evidence.
    read_report > "${REPORT_DIR}/device-report.json" || true
}

ORIGINAL_AIRPLANE=""
ORIGINAL_WIFI=""
ORIGINAL_DATA=""

restore_network()
{
    if [[ "${ORIGINAL_AIRPLANE}" == "1" ]]; then
        "${ADB[@]}" shell cmd connectivity airplane-mode enable >/dev/null 2>&1 || true
    elif [[ -n "${ORIGINAL_AIRPLANE}" ]]; then
        "${ADB[@]}" shell cmd connectivity airplane-mode disable >/dev/null 2>&1 || true
    fi
    if [[ "${ORIGINAL_WIFI}" == "1" ]]; then
        "${ADB[@]}" shell svc wifi enable >/dev/null 2>&1 || true
    elif [[ -n "${ORIGINAL_WIFI}" ]]; then
        "${ADB[@]}" shell svc wifi disable >/dev/null 2>&1 || true
    fi
    if [[ "${ORIGINAL_DATA}" == "1" ]]; then
        "${ADB[@]}" shell svc data enable >/dev/null 2>&1 || true
    elif [[ -n "${ORIGINAL_DATA}" ]]; then
        "${ADB[@]}" shell svc data disable >/dev/null 2>&1 || true
    fi
}

cleanup()
{
    local exit_code=$?
    trap - EXIT
    capture_diagnostics
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
    restore_network
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}"         restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10         >/dev/null 2>&1 || true
    exit "${exit_code}"
}
trap cleanup EXIT

"${ADB[@]}" install -r -g "${APK_PATH}" > "${REPORT_DIR}/install.txt"
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/pm-clear.txt"
"${ADB[@]}" shell pm grant "${PACKAGE_NAME}" android.permission.RECORD_AUDIO

LAUNCH_COMPONENT="$(
    "${ADB[@]}" shell cmd package resolve-activity --brief         -a android.intent.action.MAIN         -c android.intent.category.LAUNCHER         "${PACKAGE_NAME}" |
        tr -d '\r' |
        tail -n 1
)"
if [[ -z "${LAUNCH_COMPONENT}" || "${LAUNCH_COMPONENT}" != */* ]]; then
    printf 'Could not resolve launch component for %s: %s\n'         "${PACKAGE_NAME}" "${LAUNCH_COMPONENT}" >&2
    exit 1
fi

"${ADB[@]}" shell mkdir -p "${REMOTE_FILES_DIR}"
"${ADB[@]}" shell rm -f "${REMOTE_RESULT}" "${REMOTE_RESULT}.tmp" "${REMOTE_MODEL}"
timeout --signal=TERM --kill-after=15s 240s     "${ADB[@]}" push "${MODEL_CACHE_PATH}" "${REMOTE_MODEL}"     > "${REPORT_DIR}/model-push.txt" 2>&1

remote_hash="$("${ADB[@]}" shell "toybox sha256sum '${REMOTE_MODEL}'" |
    tr -d '\r' | awk '{print $1}')"
remote_size="$("${ADB[@]}" shell "toybox stat -c '%s' '${REMOTE_MODEL}'" |
    tr -d '\r')"
if [[ "${remote_hash}" != "${MODEL_SHA256}" ||
      "${remote_size}" != "${MODEL_BYTES}" ]]; then
    printf 'On-device selected model verification failed.\n' >&2
    exit 1
fi

ORIGINAL_AIRPLANE="$("${ADB[@]}" shell settings get global airplane_mode_on | tr -d '\r')"
ORIGINAL_WIFI="$("${ADB[@]}" shell settings get global wifi_on | tr -d '\r')"
ORIGINAL_DATA="$("${ADB[@]}" shell settings get global mobile_data | tr -d '\r')"

"${ADB[@]}" shell svc wifi disable
"${ADB[@]}" shell svc data disable
"${ADB[@]}" shell cmd connectivity airplane-mode enable
sleep 2

AIRPLANE_NOW="$("${ADB[@]}" shell settings get global airplane_mode_on | tr -d '\r')"
WIFI_NOW="$("${ADB[@]}" shell settings get global wifi_on | tr -d '\r')"
DATA_NOW="$("${ADB[@]}" shell settings get global mobile_data | tr -d '\r')"
{
    printf 'airplane_mode=%s\n' "${AIRPLANE_NOW}"
    printf 'wifi_on=%s\n' "${WIFI_NOW}"
    printf 'mobile_data=%s\n' "${DATA_NOW}"
} > "${REPORT_DIR}/network-disabled.txt"
if [[ "${AIRPLANE_NOW}" != "1" ||
      "${WIFI_NOW}" == "1" ||
      "${DATA_NOW}" == "1" ]]; then
    printf 'Could not prove device networking is disabled.\n' >&2
    exit 1
fi

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}"     prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20     > "${REPORT_DIR}/prepare.txt"
"${ADB[@]}" shell am start -W     -n "${LAUNCH_COMPONENT}"     -a android.intent.action.MAIN     -c android.intent.category.LAUNCHER     --ez reachy_rma190_offline_speech_e2e_acceptance true     > "${REPORT_DIR}/launch.txt"
cat "${REPORT_DIR}/launch.txt"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}"     wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30     > "${REPORT_DIR}/focus.txt"

printf '%s\n'     'PHYSICAL ACTION REQUIRED: speak a real request into the A54 microphone.'     'Recommended utterance: "Please say hello back to me and nod once."'     'Do not play a recording or inject a transcript; the harness requires the shipping microphone path.'

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
report_json=""
while true; do
    report_json="$(read_report)"
    if [[ "${report_json}" == \{* ]]; then
        break
    fi
    report_json=""
    if (( $(date +%s) >= deadline )); then
        printf 'Timed out waiting for a real physical spoken turn.\n' >&2
        exit 1
    fi
    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf 'Unity exited before spoken-turn evidence was written.\n' >&2
        exit 1
    fi
    sleep "${POLL_SECONDS}"
done

printf '%s\n' "${report_json}" > "${REPORT_DIR}/${RESULT_FILE}"
python3 - "${REPORT_DIR}/${RESULT_FILE}" <<'PY'
from __future__ import annotations

import json
import pathlib
import sys

report = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
assert report["status"] == "passed", report
assert report["selected_llm_execution"] == "OnDevice", report
assert report["local_model_id"] == "qwen3-0.6b", report
assert report["local_model_sha256"] == (
    "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e"
), report
for key in (
    "microphone_available",
    "observed_listening",
    "observed_transcribing",
    "observed_thinking",
    "observed_speaking",
    "observed_idle_after_speaking",
    "behavior_service_ready",
    "human_utterance_required",
):
    assert report[key] is True, (key, report)
assert report["behavior_revision_after"] > report["behavior_revision_before"], report
for key in (
    "synthetic_speech_injected",
    "transcript_persisted",
    "raw_audio_persisted",
    "cloud_credentials_injected",
    "system_asr_fallback_used",
    "network_tts_fallback_used",
):
    assert report[key] is False, (key, report)
assert report["final_interaction_state"] == "Idle", report
print(json.dumps(report, indent=2, sort_keys=True))
PY

capture_diagnostics
sha256sum "${APK_PATH}" > "${REPORT_DIR}/apk.sha256"
sha256sum "${REPORT_DIR}/${RESULT_FILE}" > "${REPORT_DIR}/report.sha256"
{
    printf 'device_serial=%s\n' "${DEVICE_SERIAL}"
    printf 'model=%s\n' "$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
    printf 'network_disabled_airplane=%s\n' "${AIRPLANE_NOW}"
    printf 'network_disabled_wifi=%s\n' "${WIFI_NOW}"
    printf 'network_disabled_mobile_data=%s\n' "${DATA_NOW}"
} > "${REPORT_DIR}/environment.txt"

printf '%s\n'     'RMA-190 physical on-device ASR -> local LLM -> behavior -> offline TTS spoken turn passed.'

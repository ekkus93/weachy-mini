#!/usr/bin/env bash
set -euo pipefail

# Exploratory (not a permanent CI gate): runs ReachyRma195CloudLlmThermalProbe on the
# physical device and captures dumpsys thermalservice/battery before, during, and
# after the run, so the result can be compared against RMA-135's documented
# on-device-LLM thermal baseline
# (docs/validation/RMA_135_SM_A546E_THERMAL_FINDING_2026-08-17.md).

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

APK_PATH="${1:-Builds/Android/weachy-mini-development.apk}"
REPORT_DIR="${2:-artifacts/rma195-cloud-llm-thermal-probe}"
DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:?Set REACHY_ANDROID_SERIAL to the physical Android device serial.}"
ADB_BIN="${ADB_BIN:-adb}"
PACKAGE_NAME="com.ekkus.weachymini"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT_PATH="${REMOTE_FILES_DIR}/rma195-cloud-llm-thermal-probe.json"
REMOTE_CHECKPOINT_GLOB="${REMOTE_FILES_DIR}/rma195-cloud-llm-thermal-checkpoint-*.json"
TIMEOUT_SECONDS="${RMA195_THERMAL_PROBE_TIMEOUT_SECONDS:-180}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")

mkdir -p "${REPORT_DIR}"
[[ -f "${APK_PATH}" ]] || { printf 'APK is missing: %s\n' "${APK_PATH}" >&2; exit 1; }

read_latest_checkpoint() {
  "${ADB[@]}" shell \
    "latest=\$(ls -1 ${REMOTE_CHECKPOINT_GLOB} 2>/dev/null | tail -n 1); if test -n \"\${latest}\"; then cat \"\${latest}\"; fi" \
    2>/dev/null | tr -d '\r'
}

capture_thermal() {
  local label="$1"
  timeout 15s "${ADB[@]}" shell dumpsys thermalservice > "${REPORT_DIR}/thermalservice-${label}.txt" 2>&1 || true
  timeout 15s "${ADB[@]}" shell dumpsys battery > "${REPORT_DIR}/battery-${label}.txt" 2>&1 || true
  local skin ap
  skin="$(grep -m1 'mName=SKIN' "${REPORT_DIR}/thermalservice-${label}.txt" | sed -n 's/.*mValue=\([0-9.]*\).*/\1/p')"
  ap="$(grep -m1 'mName=AP,' "${REPORT_DIR}/thermalservice-${label}.txt" | sed -n 's/.*mValue=\([0-9.]*\).*/\1/p')"
  printf 'thermal[%s]: SKIN=%s AP=%s (see thermalservice-%s.txt)\n' "${label}" "${skin:-?}" "${ap:-?}" "${label}"
}

[[ "$("${ADB[@]}" get-state | tr -d '\r\n')" == "device" ]] || {
  printf '%s\n' 'adb device is unavailable.' >&2
  exit 1
}
model="$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
printf 'device model=%s serial=%s\n' "${model}" "${DEVICE_SERIAL}"

printf '\n=== reverse tcp:11434 (phone -> host Ollama) ===\n'
"${ADB[@]}" reverse tcp:11434 tcp:11434
"${ADB[@]}" reverse --list

printf '\n=== baseline thermal (before install/launch) ===\n'
capture_thermal "before"

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
"${ADB[@]}" install -r "${APK_PATH}" > "${REPORT_DIR}/adb-install.txt"
"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package-before-launch.txt"
launch_component="$(awk '/android.intent.action.MAIN:/ {main=1; next} main && / filter / {print $2; exit}' "${REPORT_DIR}/package-before-launch.txt")"
[[ -n "${launch_component}" && "${launch_component}" == */* ]] || {
  printf '%s\n' 'Could not resolve Unity launcher activity.' >&2
  exit 1
}

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
"${ADB[@]}" shell "mkdir -p '${REMOTE_FILES_DIR}' && rm -f '${REMOTE_RESULT_PATH}' ${REMOTE_CHECKPOINT_GLOB}"
"${ADB[@]}" logcat -c >/dev/null 2>&1 || true

printf '\n=== launching with reachy_rma195_cloud_llm_thermal_probe=true ===\n'
"${ADB[@]}" shell am start -W \
  -n "${launch_component}" \
  -a android.intent.action.MAIN \
  -c android.intent.category.LAUNCHER \
  --ez reachy_rma195_cloud_llm_thermal_probe true \
  | tee "${REPORT_DIR}/launch.txt"

start="$(date +%s)"
last_checkpoint_sequence=""
mid_captured=0
while true; do
  checkpoint_json="$(read_latest_checkpoint || true)"
  if [[ -n "${checkpoint_json}" ]]; then
    printf '%s\n' "${checkpoint_json}" > "${REPORT_DIR}/checkpoint-latest.json"
    checkpoint_sequence="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("sequence",""))' <<<"${checkpoint_json}" 2>/dev/null || true)"
    checkpoint_stage="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("stage",""))' <<<"${checkpoint_json}" 2>/dev/null || true)"
    checkpoint_elapsed="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("elapsed_milliseconds",""))' <<<"${checkpoint_json}" 2>/dev/null || true)"
    if [[ "${checkpoint_sequence}" != "${last_checkpoint_sequence}" ]]; then
      printf 'checkpoint sequence=%s stage=%s elapsed_ms=%s\n' \
        "${checkpoint_sequence}" "${checkpoint_stage}" "${checkpoint_elapsed}"
      last_checkpoint_sequence="${checkpoint_sequence}"
    fi
    if [[ "${mid_captured}" == "0" && "${checkpoint_stage}" == "generation_attempt_completed" ]]; then
      printf '\n=== mid-run thermal (during sustained cloud LLM generation) ===\n'
      capture_thermal "during"
      mid_captured=1
    fi
  fi

  json="$("${ADB[@]}" shell "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" | tr -d '\r' || true)"
  if [[ -n "${json}" ]]; then
    printf '%s\n' "${json}" > "${REPORT_DIR}/rma195-cloud-llm-thermal-probe.json"
    break
  fi
  "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null || {
    printf 'Unity exited before the probe report was written; last checkpoint=%s.\n' \
      "${last_checkpoint_sequence:-none}" >&2
    capture_thermal "after-crash"
    exit 1
  }
  if (( $(date +%s) - start >= TIMEOUT_SECONDS )); then
    printf 'Probe timed out after %ss; last checkpoint=%s.\n' "${TIMEOUT_SECONDS}" "${last_checkpoint_sequence:-none}" >&2
    capture_thermal "after-timeout"
    exit 1
  fi
  sleep 2
done

printf '\n=== immediate post-run thermal ===\n'
capture_thermal "after"

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true

printf '\n=== probe report ===\n'
cat "${REPORT_DIR}/rma195-cloud-llm-thermal-probe.json"
status="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("status",""))' < "${REPORT_DIR}/rma195-cloud-llm-thermal-probe.json")"
[[ "${status}" == "completed" ]] || {
  printf 'Probe reported status=%s (expected completed).\n' "${status}" >&2
  exit 1
}
printf '\nRMA-195 cloud LLM thermal probe completed on %s (%s).\n' "${DEVICE_SERIAL}" "${model}"

#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

APK_PATH="${1:-artifacts/android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${2:-artifacts/rma135-resource-governor-android}"
DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:?Set REACHY_ANDROID_SERIAL to the dedicated physical Android device serial.}"
ADB_BIN="${ADB_BIN:-adb}"
PACKAGE_NAME="com.ekkus.weachymini"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT_PATH="${REMOTE_FILES_DIR}/rma135-resource-governor-acceptance.json"
REMOTE_MODEL_PATH="${REMOTE_FILES_DIR}/rma135-qwen3-0.6b-q4_k_m.gguf"
REMOTE_CHECKPOINT_GLOB="${REMOTE_FILES_DIR}/rma135-resource-governor-checkpoint-*.json"
MODEL_CACHE_ROOT="${RMA133_MODEL_CACHE_ROOT:-${HOME}/.cache/weachy-mini/rma133/models}"
TIMEOUT_SECONDS="${RMA135_ACCEPTANCE_TIMEOUT_SECONDS:-600}"
EXPECTED_DEVICE_MODEL="${RMA135_EXPECTED_DEVICE_MODEL:-LG-H872}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")

mkdir -p "${REPORT_DIR}" "${MODEL_CACHE_ROOT}"
[[ -f "${APK_PATH}" ]] || { printf 'RMA-135 APK is missing: %s\n' "${APK_PATH}" >&2; exit 1; }
command -v "${ADB_BIN}" >/dev/null 2>&1 || { printf 'adb unavailable: %s\n' "${ADB_BIN}" >&2; exit 1; }
command -v python3 >/dev/null 2>&1 || { printf '%s\n' 'python3 is required.' >&2; exit 1; }
command -v curl >/dev/null 2>&1 || { printf '%s\n' 'curl is required.' >&2; exit 1; }

mapfile -t MODEL_FIELDS < <(python3 - "benchmarks/rma133/candidates-v6.json" <<'PY'
import json
import sys
from pathlib import Path

config = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
items = [c for c in config["candidates"] if c.get("candidate_id") == "qwen3-0.6b-q4-k-m"]
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
(( ${#MODEL_FIELDS[@]} == 6 )) || { printf '%s\n' 'Could not resolve selected RMA-133 model.' >&2; exit 1; }
MODEL_ID="${MODEL_FIELDS[0]}"
MODEL_FILENAME="${MODEL_FIELDS[1]}"
MODEL_URL="${MODEL_FIELDS[2]}"
MODEL_BYTES="${MODEL_FIELDS[3]}"
MODEL_SHA256="${MODEL_FIELDS[4]}"
MODEL_SUFFIX="${MODEL_FIELDS[5]}"
[[ "${MODEL_ID}" == "qwen3-0.6b" && "${MODEL_BYTES}" == "396704416" && \
   "${MODEL_SHA256}" == "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e" && \
   "${MODEL_SUFFIX}" == "/no_think" ]] || {
  printf '%s\n' 'Frozen RMA-135 selected-model contract drifted.' >&2
  exit 1
}
MODEL_CACHE_PATH="${MODEL_CACHE_ROOT}/${MODEL_SHA256}-${MODEL_FILENAME}"
verify_model() {
  [[ -f "$1" ]] &&
    [[ "$(stat -c '%s' "$1")" == "${MODEL_BYTES}" ]] &&
    [[ "$(sha256sum "$1" | awk '{print $1}')" == "${MODEL_SHA256}" ]]
}
if [[ -e "${MODEL_CACHE_PATH}" ]] && ! verify_model "${MODEL_CACHE_PATH}"; then
  printf 'Cached selected model is invalid: %s\n' "${MODEL_CACHE_PATH}" >&2
  exit 1
fi
if [[ ! -e "${MODEL_CACHE_PATH}" ]]; then
  tmp="${MODEL_CACHE_PATH}.partial.$$"
  rm -f -- "${tmp}"
  curl --fail --location --silent --show-error --output "${tmp}" "${MODEL_URL}"
  verify_model "${tmp}" || {
    rm -f -- "${tmp}"
    printf '%s\n' 'Downloaded selected model failed exact validation.' >&2
    exit 1
  }
  mv -f -- "${tmp}" "${MODEL_CACHE_PATH}"
fi
verify_model "${MODEL_CACHE_PATH}" || { printf '%s\n' 'Selected model cache preparation failed.' >&2; exit 1; }

read_latest_checkpoint() {
  "${ADB[@]}" shell \
    "latest=\$(ls -1 ${REMOTE_CHECKPOINT_GLOB} 2>/dev/null | tail -n 1); if test -n \"\${latest}\"; then cat \"\${latest}\"; fi" \
    2>/dev/null | tr -d '\r'
}

capture() {
  set +e
  timeout 10s "${ADB[@]}" get-state > "${REPORT_DIR}/adb-state.txt" 2>&1
  "${ADB_BIN}" devices -l > "${REPORT_DIR}/adb-devices.txt" 2>&1
  timeout 10s "${ADB[@]}" shell getprop > "${REPORT_DIR}/getprop.txt" 2>&1
  timeout 15s "${ADB[@]}" logcat -d -v threadtime > "${REPORT_DIR}/logcat.txt" 2>&1
  timeout 15s "${ADB[@]}" shell dumpsys battery > "${REPORT_DIR}/battery.txt" 2>&1
  timeout 15s "${ADB[@]}" shell dumpsys meminfo "${PACKAGE_NAME}" > "${REPORT_DIR}/meminfo.txt" 2>&1
  timeout 15s "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt" 2>&1
  timeout 15s "${ADB[@]}" shell dumpsys thermalservice > "${REPORT_DIR}/thermalservice.txt" 2>&1
  timeout 15s "${ADB[@]}" shell dumpsys activity activities > "${REPORT_DIR}/activity.txt" 2>&1
  timeout 10s "${ADB[@]}" shell pidof "${PACKAGE_NAME}" > "${REPORT_DIR}/pidof.txt" 2>&1
  timeout 10s "${ADB[@]}" shell \
    "ls -1 ${REMOTE_CHECKPOINT_GLOB} 2>/dev/null || true" \
    | tr -d '\r' > "${REPORT_DIR}/checkpoint-files.txt" 2>&1
  mkdir -p "${REPORT_DIR}/checkpoints"
  while IFS= read -r checkpoint_path; do
    [[ -n "${checkpoint_path}" ]] || continue
    checkpoint_name="$(basename -- "${checkpoint_path}")"
    timeout 10s "${ADB[@]}" pull "${checkpoint_path}" \
      "${REPORT_DIR}/checkpoints/${checkpoint_name}" >/dev/null 2>&1 || true
  done < "${REPORT_DIR}/checkpoint-files.txt"
  read_latest_checkpoint > "${REPORT_DIR}/checkpoint-latest.json" 2>/dev/null || true
  timeout 10s "${ADB[@]}" shell \
    "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" \
    | tr -d '\r' > "${REPORT_DIR}/rma135-resource-governor-acceptance-latest.json" 2>/dev/null
  set -e
}

on_exit() {
  local code=$?
  trap - EXIT
  if (( code != 0 )); then
    capture
  fi
  exit "${code}"
}
trap on_exit EXIT

[[ "$("${ADB[@]}" get-state | tr -d '\r\n')" == "device" ]] || {
  printf '%s\n' 'RMA-135 adb device is unavailable.' >&2
  exit 1
}
model="$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
abi="$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
sdk="$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
qemu="$("${ADB[@]}" shell getprop ro.kernel.qemu | tr -d '\r')"
hardware="$("${ADB[@]}" shell getprop ro.hardware | tr -d '\r')"
[[ "${model}" == "${EXPECTED_DEVICE_MODEL}" && "${abi}" == "arm64-v8a" && "${sdk}" == "26" ]] || {
  printf 'Device mismatch model=%s abi=%s sdk=%s\n' "${model}" "${abi}" "${sdk}" >&2
  exit 1
}
[[ "${qemu}" != "1" && "${hardware,,}" != *goldfish* && "${hardware,,}" != *ranchu* ]] || {
  printf '%s\n' 'Emulator evidence refused.' >&2
  exit 1
}
printf 'serial=%s\nmodel=%s\nabi=%s\nsdk=%s\nhardware=%s\n' \
  "${DEVICE_SERIAL}" "${model}" "${abi}" "${sdk}" "${hardware}" \
  > "${REPORT_DIR}/device.txt"

APK_SHA256="$(sha256sum "${APK_PATH}" | awk '{print $1}')"
printf '%s  %s\n' "${APK_SHA256}" "${APK_PATH}" > "${REPORT_DIR}/apk-sha256.txt"
sha256sum "${MODEL_CACHE_PATH}" > "${REPORT_DIR}/model-host-sha256.txt"

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
"${ADB[@]}" install -r "${APK_PATH}" > "${REPORT_DIR}/adb-install.txt"
mapfile -t INSTALLED_APK_PATHS < <(
  "${ADB[@]}" shell pm path "${PACKAGE_NAME}" | tr -d '\r' | sed -n 's/^package://p'
)
if (( ${#INSTALLED_APK_PATHS[@]} != 1 )); then
  printf 'Expected one installed base APK; found %s.\n' "${#INSTALLED_APK_PATHS[@]}" >&2
  exit 1
fi
INSTALLED_APK_PATH="${INSTALLED_APK_PATHS[0]}"
INSTALLED_APK_SHA256="$(
  "${ADB[@]}" shell "toybox sha256sum '${INSTALLED_APK_PATH}'" | tr -d '\r' | awk '{print $1}'
)"
printf 'host_sha256=%s\ndevice_path=%s\ndevice_sha256=%s\n' \
  "${APK_SHA256}" "${INSTALLED_APK_PATH}" "${INSTALLED_APK_SHA256}" \
  > "${REPORT_DIR}/installed-apk-verification.txt"
[[ "${INSTALLED_APK_SHA256}" == "${APK_SHA256}" ]] || {
  printf 'Installed APK SHA-256 mismatch: host=%s device=%s.\n' \
    "${APK_SHA256}" "${INSTALLED_APK_SHA256}" >&2
  exit 1
}

"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package-before-launch.txt"
launch_component="$(awk '/android.intent.action.MAIN:/ {main=1; next} main && / filter / {print $2; exit}' "${REPORT_DIR}/package-before-launch.txt")"
[[ -n "${launch_component}" && "${launch_component}" == */* ]] || {
  printf '%s\n' 'Could not resolve Unity launcher activity.' >&2
  exit 1
}
printf '%s\n' "${launch_component}" > "${REPORT_DIR}/launch-component.txt"

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/pm-clear.txt"
"${ADB[@]}" shell \
  "mkdir -p '${REMOTE_FILES_DIR}' && rm -f '${REMOTE_RESULT_PATH}' '${REMOTE_MODEL_PATH}' ${REMOTE_CHECKPOINT_GLOB}"
timeout --signal=TERM --kill-after=15s 240s \
  "${ADB[@]}" push "${MODEL_CACHE_PATH}" "${REMOTE_MODEL_PATH}" \
  > "${REPORT_DIR}/model-push.txt" 2>&1
remote="$(
  "${ADB[@]}" shell \
    "toybox sha256sum '${REMOTE_MODEL_PATH}'; toybox stat -c '%s' '${REMOTE_MODEL_PATH}'" \
    | tr -d '\r'
)"
printf '%s\n' "${remote}" > "${REPORT_DIR}/model-device-verification.txt"
[[ "$(printf '%s\n' "${remote}" | sed -n '1s/[[:space:]].*$//p')" == "${MODEL_SHA256}" && \
   "$(printf '%s\n' "${remote}" | sed -n '2p')" == "${MODEL_BYTES}" ]] || {
  printf '%s\n' 'On-device selected model verification failed.' >&2
  exit 1
}

"${ADB[@]}" logcat -c >/dev/null 2>&1 || true
"${ADB[@]}" shell am start -W \
  -n "${launch_component}" \
  -a android.intent.action.MAIN \
  -c android.intent.category.LAUNCHER \
  --ez reachy_rma135_resource_governor_acceptance true \
  > "${REPORT_DIR}/launch.txt" 2>&1
cat "${REPORT_DIR}/launch.txt"
grep -Eq '(^|[[:space:]])(Error:|Exception)' "${REPORT_DIR}/launch.txt" && {
  printf '%s\n' 'RMA-135 Unity launch reported an error.' >&2
  exit 1
}

start="$(date +%s)"
last_checkpoint_sequence=""
last_checkpoint_stage="none"
last_checkpoint_elapsed=""
while true; do
  checkpoint_json="$(read_latest_checkpoint || true)"
  if [[ -n "${checkpoint_json}" ]]; then
    printf '%s\n' "${checkpoint_json}" > "${REPORT_DIR}/checkpoint-latest.json"
    mapfile -t checkpoint_fields < <(python3 - "${checkpoint_json}" <<'PY'
import json
import sys

checkpoint = json.loads(sys.argv[1])
print(checkpoint.get("sequence", ""))
print(checkpoint.get("stage", ""))
print(checkpoint.get("elapsed_milliseconds", ""))
PY
)
    checkpoint_sequence="${checkpoint_fields[0]:-}"
    checkpoint_stage="${checkpoint_fields[1]:-unknown}"
    checkpoint_elapsed="${checkpoint_fields[2]:-}"
    if [[ "${checkpoint_sequence}" != "${last_checkpoint_sequence}" ]]; then
      printf 'RMA-135 checkpoint sequence=%s stage=%s elapsed_ms=%s\n' \
        "${checkpoint_sequence}" "${checkpoint_stage}" "${checkpoint_elapsed}"
      last_checkpoint_sequence="${checkpoint_sequence}"
      last_checkpoint_stage="${checkpoint_stage}"
      last_checkpoint_elapsed="${checkpoint_elapsed}"
    fi
  fi

  json="$(
    "${ADB[@]}" shell \
      "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" \
      | tr -d '\r' || true
  )"
  if [[ -n "${json}" ]]; then
    printf '%s\n' "${json}" > "${REPORT_DIR}/rma135-resource-governor-acceptance.json"
    status="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("status",""))' <<<"${json}")"
    [[ "${status}" == "passed" ]] && break
    printf 'RMA-135 device report status=%s: %s\n' "${status}" "${json}" >&2
    exit 1
  fi
  "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null || {
    printf 'Unity exited before RMA-135 report; last checkpoint=%s elapsed_ms=%s.\n' \
      "${last_checkpoint_stage}" "${last_checkpoint_elapsed:-unknown}" >&2
    exit 1
  }
  if (( $(date +%s) - start >= TIMEOUT_SECONDS )); then
    printf 'RMA-135 device acceptance timed out; last checkpoint=%s sequence=%s elapsed_ms=%s.\n' \
      "${last_checkpoint_stage}" "${last_checkpoint_sequence:-none}" \
      "${last_checkpoint_elapsed:-unknown}" >&2
    exit 1
  fi
  sleep 2
done

python3 - "${REPORT_DIR}/rma135-resource-governor-acceptance.json" <<'PY'
import json
import math
import sys
from pathlib import Path

r = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
assert r["status"] == "passed", r
assert r["android_api_level"] == 26, r
assert r["reachy_llama_abi"] == 2, r
assert r["model_id"] == "qwen3-0.6b", r
assert r["artifact_sha256"] == "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e", r
assert r["artifact_bytes"] == 396704416, r
assert r["total_memory_bytes"] > 0, r
assert 0 <= r["initial_available_memory_bytes"] <= r["total_memory_bytes"], r
assert 0 <= r["final_available_memory_bytes"] <= r["total_memory_bytes"], r
assert r["low_memory_threshold_bytes"] >= 0, r
assert r["logical_processor_count"] > 0, r
assert r["thermal_status_initial"] == "Unavailable", r
assert r["thermal_status_final"] == "Unavailable", r
assert r["admission_device_profile"] == "Conservative", r
assert r["effective_context_tokens"] <= 1024, r
assert r["effective_batch_tokens"] <= 128, r
assert r["effective_micro_batch_tokens"] <= 64, r
assert r["effective_threads"] <= 2 and r["effective_batch_threads"] <= 2, r
assert r["startup_physics_observations"] >= 3, r
assert r["startup_physics_exceeded_observations"] >= 0, r
assert r["startup_physics_state"] in {"Healthy", "AtRisk"}, r
assert r["production_runtime_model_hash"] > 0, r
assert 0 <= r["post_load_available_memory_bytes"] <= r["total_memory_bytes"], r
assert 1 <= r["post_load_stabilization_observations"] <= 12, r
assert r["post_load_stabilized_mode"] != "Suspended", r
assert r["physics_fault_injection_kind"] == "controlled_one_shot_budget_exceeded", r
assert r["physics_fault_injection_count"] == 1, r
assert r["fault_injection_governed_status"] == "ResourceCancelledDuringGeneration", r
assert r["fault_injection_provider_status"] == "Cancelled", r
assert r["worker_steps_after_injection"] > r["worker_steps_before_injection"], r
assert math.isfinite(r["worker_accumulated_lag_seconds_after_injection"]), r
assert math.isfinite(r["worker_last_step_microseconds_after_injection"]), r
assert math.isfinite(r["worker_max_step_microseconds_after_injection"]), r
assert 1 <= r["recovery_observations"] <= 8, r
assert r["recovery_mode"] != "Suspended", r
assert r["post_recovery_governed_status"] == "ProviderCompleted", r
assert r["post_recovery_provider_status"] == "Succeeded", r
assert r["post_recovery_stream_text_events"] > 0, r
assert r["post_recovery_prompt_tokens"] > 0, r
assert r["post_recovery_generated_tokens"] > 0, r
assert r["final_worker_steps"] > r["worker_steps_after_injection"], r
assert r["final_physics_budget_state"] in {"Healthy", "AtRisk"}, r
assert r["network_fallback_used"] is False, r
assert r["automatic_retry_used"] is False, r
assert r["physics_timestep_modified"] is False, r
assert r["json_repair_used"] is False, r
assert r["report_contains_prompt_or_response_content"] is False, r
for forbidden in ("prompt", "speech", "response_json", "messages"):
    assert forbidden not in r, (forbidden, r.keys())
print(json.dumps(r, indent=2, sort_keys=True))
PY

capture
"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
trap - EXIT
printf 'RMA-135 resource governor physical acceptance passed on %s (%s).\n' \
  "${DEVICE_SERIAL}" "${model}"

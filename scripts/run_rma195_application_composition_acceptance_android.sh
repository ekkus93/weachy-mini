#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA195_COMPOSITION_REPORT_DIR:-${ROOT_DIR}/build/rma195-application-composition-report}"
PACKAGE_NAME="com.ekkus.weachymini"
ACTIVITY="com.unity3d.player.UnityPlayerGameActivity"
RESULT_FILE_NAME="rma195-application-composition-acceptance.json"
REMOTE_RESULT_PATH="/sdcard/Android/data/${PACKAGE_NAME}/files/${RESULT_FILE_NAME}"
TIMEOUT_SECONDS="${RMA195_COMPOSITION_TIMEOUT_SECONDS:-90}"

if [[ ! -s "${APK_PATH}" ]]; then
  printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
  exit 1
fi
command -v "${ADB_BIN}" >/dev/null

select_device_serial()
{
  mapfile -t serials < <(
    "${ADB_BIN}" devices \
      | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
  )
  local -a accepted=()
  local serial
  for serial in "${serials[@]}"; do
    local state model abi sdk
    state="$("${ADB_BIN}" -s "${serial}" get-state | tr -d '\r')"
    model="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.model | tr -d '\r')"
    abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
    sdk="$("${ADB_BIN}" -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'candidate=%s state=%s model=%s abi=%s sdk=%s\n' \
      "${serial}" "${state}" "${model}" "${abi}" "${sdk}" >&2
    if [[ "${state}" == "device" && "${abi}" == "arm64-v8a" && \
          "${sdk}" =~ ^[0-9]+$ ]] && (( sdk >= 31 )); then
      accepted+=("${serial}")
    fi
  done
  if (( ${#accepted[@]} != 1 )); then
    printf 'Expected exactly one physical arm64-v8a API-31+ device; found %s.\n' \
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

read_report_run_as()
{
  "${ADB[@]}" exec-out run-as "${PACKAGE_NAME}" sh -c \
    "test -f '${REMOTE_RESULT_PATH}' && cat '${REMOTE_RESULT_PATH}'" \
    2>/dev/null | tr -d '\r' || true
}

read_report_shell()
{
  "${ADB[@]}" shell \
    "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" \
    2>/dev/null | tr -d '\r' || true
}

capture()
{
  "${ADB[@]}" logcat -d -v threadtime > "${REPORT_DIR}/logcat.txt" || true
  "${ADB[@]}" shell getprop > "${REPORT_DIR}/getprop.txt" || true
  "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt" || true
  "${ADB[@]}" shell dumpsys activity activities > "${REPORT_DIR}/activity.txt" || true
  "${ADB[@]}" shell dumpsys audio > "${REPORT_DIR}/audio.txt" || true
  read_report_run_as > "${REPORT_DIR}/device-report-run-as.json" || true
  read_report_shell > "${REPORT_DIR}/device-report-shell.json" || true
}

cleanup()
{
  local code=$?
  trap - EXIT
  if (( code != 0 )); then
    capture
  fi
  set +e
  "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1
  exit "${code}"
}
trap cleanup EXIT

"${ADB[@]}" wait-for-device
model="$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
sdk="$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
abi="$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
printf 'rma195_device=%s model=%s sdk=%s abi=%s\n' \
  "${DEVICE_SERIAL}" "${model}" "${sdk}" "${abi}"
if [[ "${model}" != "SM-A546E" ]]; then
  printf 'RMA-195 physical closure is pinned to the Samsung A54 SM-A546E; got %s.\n' \
    "${model}" >&2
  exit 1
fi
if [[ ! "${sdk}" =~ ^[0-9]+$ ]] || (( sdk < 31 )); then
  printf 'RMA-195 physical closure requires API 31+ explicit on-device ASR; got %s.\n' \
    "${sdk}" >&2
  exit 1
fi
if [[ "${abi}" != "arm64-v8a" ]]; then
  printf 'RMA-195 physical closure requires arm64-v8a; got %s.\n' "${abi}" >&2
  exit 1
fi

"${ADB[@]}" install -r -t "${APK_PATH}" > "${REPORT_DIR}/install.txt"
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/pm-clear.txt"
"${ADB[@]}" shell pm grant "${PACKAGE_NAME}" android.permission.CAMERA
"${ADB[@]}" shell pm grant "${PACKAGE_NAME}" android.permission.RECORD_AUDIO
"${ADB[@]}" shell rm -f "${REMOTE_RESULT_PATH}" >/dev/null 2>&1 || true
"${ADB[@]}" logcat -c >/dev/null 2>&1 || true

"${ADB[@]}" shell am start -W \
  -n "${PACKAGE_NAME}/${ACTIVITY}" \
  -a android.intent.action.MAIN \
  -c android.intent.category.LAUNCHER \
  --ez reachy_rma195_application_composition_acceptance true \
  > "${REPORT_DIR}/launch.txt"
cat "${REPORT_DIR}/launch.txt"

start="$(date +%s)"
report_json=""
while true; do
  report_json="$(read_report_run_as)"
  if [[ -z "${report_json}" ]]; then
    report_json="$(read_report_shell)"
  fi
  if [[ "${report_json}" == \{* ]]; then
    break
  fi
  report_json=""
  "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null || {
    printf '%s\n' 'Unity exited before the RMA-195 report was produced.' >&2
    exit 1
  }
  if (( $(date +%s) - start >= TIMEOUT_SECONDS )); then
    printf '%s\n' 'RMA-195 application-composition acceptance timed out.' >&2
    exit 1
  fi
  sleep 1
done

printf '%s\n' "${report_json}" > \
  "${REPORT_DIR}/rma195-application-composition-acceptance.json"
capture

if ! python3 - "${REPORT_DIR}/rma195-application-composition-acceptance.json" <<'PY'
import json
import sys
from pathlib import Path

r = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
assert r["schema_version"] == 1, r
assert r["status"] == "passed", r
assert r["device_model"] == "samsung SM-A546E", r
assert r["android_api_level"] >= 31, r
assert r["application_state"] in {"Ready", "Degraded"}, r
expected = {
    "simulation_service": "ReachyProductionSimulationApplicationService",
    "camera_service": "ReachyDiscoveredCameraApplicationService",
    "audio_service": "ReachyAndroidSpeechCapabilityApplicationService",
    "provider_service": "ReachyLocalLlmProviderApplicationService",
    "perception_service": "ReachyAndroidPerceptionApplicationService",
    "behavior_service": "ReachyBaselineBehaviorApplicationService",
    "persistence_service": "ReachySettingsPersistenceApplicationService",
    "ui_service": "ReachySettingsMainScreenApplicationService",
}
for key, value in expected.items():
    assert r[key] == value, (key, r)
for key in (
    "local_llm_capability",
    "cloud_llm_capability",
    "cloud_vlm_capability",
    "full_behavior_intent_capability",
    "full_behavior_intent_accepted",
    "front_camera_available",
    "rear_camera_available",
    "selected_tts_voice_installed",
    "tts_started",
    "tts_completed",
):
    assert r[key] is True, (key, r)
assert r["behavior_health"] != "Faulted", r
assert r["camera_permission"] == "Granted", r
assert r["available_camera_count"] >= 2, r
assert r["on_device_asr_state"] == "Available", r
assert r["offline_tts_state"] == "Available", r
assert r["selected_tts_voice_id"], r
assert r["selected_tts_voice_may_use_network"] is False, r
assert r["human_asr_transcript_injected"] is False, r
assert r["cloud_credentials_injected"] is False, r
assert r["local_model_injected"] is False, r
print(json.dumps(r, indent=2, sort_keys=True))
PY
then
  printf '%s\n' 'RMA-195 report JSON validation failed.' >&2
  exit 1
fi

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
trap - EXIT
printf 'RMA-195 application-composition physical acceptance passed on %s (%s).\n' \
  "${DEVICE_SERIAL}" "${model}"

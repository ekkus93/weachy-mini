#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${UNITY_LIFECYCLE_REPORT_DIR:-${ROOT_DIR}/build/unity-lifecycle-device-report}"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
PACKAGE_NAME="com.ekkus.weachymini"
LAUNCH_EXTRA_NAME="weachy_lifecycle_acceptance"
RESULT_FILE_NAME="weachy-native-lifecycle-acceptance.json"
REMOTE_RESULT_PATH="/sdcard/Android/data/${PACKAGE_NAME}/files/${RESULT_FILE_NAME}"
TIMEOUT_SECONDS="${UNITY_LIFECYCLE_TIMEOUT_SECONDS:-180}"
SUSPEND_SECONDS="${UNITY_LIFECYCLE_SUSPEND_SECONDS:-3}"
POLL_SECONDS="${UNITY_LIFECYCLE_POLL_SECONDS:-0.2}"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! -s "${FOREGROUND_HELPER}" ]]; then
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
fi
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'Lifecycle timeout must be a positive integer: %s\n' "${TIMEOUT_SECONDS}" >&2
    exit 1
fi
if [[ ! "${SUSPEND_SECONDS}" =~ ^[0-9]+$ ]] || (( SUSPEND_SECONDS < 2 )); then
    printf 'Lifecycle suspend duration must be an integer of at least 2 seconds: %s\n' \
        "${SUSPEND_SECONDS}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null

select_device_serial()
{
    mapfile -t physical_serials < <(
        "${ADB_BIN}" devices \
            | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )
    local -a arm64_serials=()
    local serial
    for serial in "${physical_serials[@]}"; do
        local abi
        local sdk
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
        sdk="$("${ADB_BIN}" -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\r')"
        printf 'physical_device=%s abi=%s sdk=%s\n' "${serial}" "${abi}" "${sdk}"
        if [[ "${abi}" == "arm64-v8a" && "${sdk}" =~ ^[0-9]+$ ]] && (( sdk >= 26 )); then
            arm64_serials+=("${serial}")
        fi
    done
    if (( ${#arm64_serials[@]} != 1 )); then
        printf 'Expected one physical arm64-v8a API-26+ device; found %s.\n' \
            "${#arm64_serials[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi
    printf '%s\n' "${arm64_serials[0]}"
}

read_report_field()
{
    local report_json="$1"
    local field="$2"
    python3 - "${report_json}" "${field}" <<'PY'
from __future__ import annotations

import json
import sys

try:
    report = json.loads(sys.argv[1])
except json.JSONDecodeError:
    print("")
else:
    value = report.get(sys.argv[2], "")
    if isinstance(value, bool):
        print("true" if value else "false")
    else:
        print(value)
PY
}

DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:-$(select_device_serial | tail -n 1)}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")
rm -rf -- "${REPORT_DIR}"
mkdir -p "${REPORT_DIR}"

capture_diagnostics()
{
    set +e
    "${ADB[@]}" logcat -d -v threadtime > "${REPORT_DIR}/logcat.txt"
    "${ADB[@]}" shell dumpsys activity activities > "${REPORT_DIR}/activity.txt"
    "${ADB[@]}" shell dumpsys window windows > "${REPORT_DIR}/window.txt"
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt"
    "${ADB[@]}" shell ps > "${REPORT_DIR}/processes.txt"
    "${ADB[@]}" shell \
        "ls -laR '/sdcard/Android/data/${PACKAGE_NAME}' 2>&1" \
        > "${REPORT_DIR}/external-files.txt"
    "${ADB[@]}" shell \
        "run-as '${PACKAGE_NAME}' sh -c 'pwd; find . -maxdepth 4 -type f -print' 2>&1" \
        > "${REPORT_DIR}/internal-files.txt"
    "${ADB[@]}" exec-out screencap -p > "${REPORT_DIR}/device-screen.png"
}

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
    if (( exit_code != 0 )); then
        capture_diagnostics
    fi
    restore_device
    exit "${exit_code}"
}
trap on_exit EXIT

read_device_report()
{
    local report_json
    report_json="$(
        "${ADB[@]}" shell \
            "if test -f '${REMOTE_RESULT_PATH}'; then cat '${REMOTE_RESULT_PATH}'; fi" \
            | tr -d '\r' \
            || true
    )"
    if [[ -n "${report_json}" ]]; then
        printf '%s' "${report_json}"
        return
    fi

    report_json="$(
        "${ADB[@]}" shell \
            "run-as '${PACKAGE_NAME}' cat 'files/${RESULT_FILE_NAME}' 2>/dev/null" \
            | tr -d '\r' \
            || true
    )"
    printf '%s' "${report_json}"
}

launch_application()
{
    local suffix="$1"
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20
    "${ADB[@]}" shell am start -W \
        -n "${launch_component}" \
        -a android.intent.action.MAIN \
        -c android.intent.category.LAUNCHER \
        --ez "${LAUNCH_EXTRA_NAME}" true \
        > "${REPORT_DIR}/launch-${suffix}.txt"
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
        > "${REPORT_DIR}/focus-${suffix}.txt"
}

{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'manufacturer=%s\n' "$("${ADB[@]}" shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' "$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
    printf 'android_release=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.release | tr -d '\r')"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
} > "${REPORT_DIR}/device.txt"

"${ADB[@]}" install -r "${APK_PATH}" > "${REPORT_DIR}/install.txt"
"${ADB[@]}" shell pm path "${PACKAGE_NAME}" > "${REPORT_DIR}/package-path.txt"
"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/package-before-launch.txt"
launch_component="$(
    awk '
        /android.intent.action.MAIN:/ { in_main = 1; next }
        in_main && / filter / { print $2; exit }
    ' "${REPORT_DIR}/package-before-launch.txt"
)"
if [[ -z "${launch_component}" || "${launch_component}" != */* ]]; then
    printf 'Could not resolve the installed Unity launcher activity.\n' >&2
    exit 1
fi
printf '%s\n' "${launch_component}" > "${REPORT_DIR}/launch-component.txt"

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/clear.txt"
"${ADB[@]}" shell rm -f "${REMOTE_RESULT_PATH}" || true
"${ADB[@]}" logcat -c || true
launch_application initial

start_epoch="$(date +%s)"
report_json=""
last_report_json=""
last_backgrounded_cycle=0
while true; do
    report_json="$(read_device_report)"
    if [[ -n "${report_json}" ]]; then
        last_report_json="${report_json}"
        printf '%s\n' "${report_json}" > "${REPORT_DIR}/lifecycle-latest.json"
        report_status="$(read_report_field "${report_json}" status)"
        report_stage="$(read_report_field "${report_json}" stage)"
        report_cycle="$(read_report_field "${report_json}" cycle)"
        case "${report_status}" in
            ok)
                break
                ;;
            failed)
                printf '%s\n' "${report_json}" > "${REPORT_DIR}/lifecycle.json"
                printf 'RMA-022 lifecycle acceptance failed: %s\n' "${report_json}" >&2
                exit 1
                ;;
            in_progress)
                if [[ "${report_stage}" == "awaiting_pause" && \
                    "${report_cycle}" =~ ^[0-9]+$ ]] && \
                    (( report_cycle > last_backgrounded_cycle )); then
                    last_backgrounded_cycle="${report_cycle}"
                    "${ADB[@]}" shell input keyevent 3
                    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
                        wait-background "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
                        > "${REPORT_DIR}/background-focus-cycle-${report_cycle}.txt"
                    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
                        printf 'Unity process exited while backgrounded in cycle %s.\n' \
                            "${report_cycle}" >&2
                        exit 1
                    fi
                    sleep "${SUSPEND_SECONDS}"
                    launch_application "resume-cycle-${report_cycle}"
                fi
                ;;
            *)
                printf 'Invalid lifecycle report: %s\n' "${report_json}" >&2
                exit 1
                ;;
        esac
    fi

    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf '%s\n' 'Unity application exited before lifecycle acceptance completed.' >&2
        exit 1
    fi
    now_epoch="$(date +%s)"
    if (( now_epoch - start_epoch >= TIMEOUT_SECONDS )); then
        if [[ -n "${last_report_json}" ]]; then
            printf 'Timed out after %s seconds at lifecycle report: %s\n' \
                "${TIMEOUT_SECONDS}" "${last_report_json}" >&2
        else
            printf 'Timed out after %s seconds before lifecycle evidence was published.\n' \
                "${TIMEOUT_SECONDS}" >&2
        fi
        exit 1
    fi
    sleep "${POLL_SECONDS}"
done

printf '%s\n' "${report_json}" > "${REPORT_DIR}/lifecycle.json"
python3 - "${REPORT_DIR}/lifecycle.json" "${SUSPEND_SECONDS}" <<'PY'
from __future__ import annotations

import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
requested_suspend = float(sys.argv[2])
report = json.loads(path.read_text(encoding="utf-8"))
if report.get("status") != "ok":
    raise SystemExit(f"lifecycle status is not ok: {report}")
if report.get("native_abi_version") != 2:
    raise SystemExit(f"unexpected native ABI: {report}")
if not report.get("native_version"):
    raise SystemExit(f"native version string is missing: {report}")
if not str(report.get("model_hash", "")).isdigit() or int(report["model_hash"]) == 0:
    raise SystemExit(f"invalid model hash: {report}")
required_true = (
    "controlled_initialization_failure_observed",
    "valid_probe_stepped",
    "probe_session_destroyed",
    "operation_after_close_rejected",
    "production_runtime_destroyed",
    "renderer_disabled_after_shutdown",
)
for key in required_true:
    if report.get(key) is not True:
        raise SystemExit(f"required lifecycle flag {key} is false: {report}")
if report.get("hidden_native_fallback") is not False:
    raise SystemExit(f"hidden native fallback was reported: {report}")
if not report.get("controlled_initialization_failure_code"):
    raise SystemExit(f"controlled failure code is missing: {report}")
if not report.get("controlled_initialization_failure_message"):
    raise SystemExit(f"controlled failure message is missing: {report}")
if report.get("pause_callback_count", 0) < 2 or report.get("resume_callback_count", 0) < 2:
    raise SystemExit(f"pause/resume callbacks were not repeated: {report}")
cycles = report.get("cycles") or []
if report.get("pause_resume_cycle_count") != 2 or len(cycles) != 2:
    raise SystemExit(f"unexpected lifecycle cycle count: {report}")
for index, cycle in enumerate(cycles, start=1):
    if cycle.get("cycle") != index:
        raise SystemExit(f"lifecycle cycle ordering is wrong: {report}")
    if cycle.get("pause_callback_observed") is not True:
        raise SystemExit(f"pause callback missing for cycle {index}: {report}")
    if cycle.get("resume_callback_observed") is not True:
        raise SystemExit(f"resume callback missing for cycle {index}: {report}")
    suspended = float(cycle.get("suspended_wall_seconds", 0.0))
    advanced = float(cycle.get("simulation_time_advance", -1.0))
    excluded = float(cycle.get("excluded_suspended_seconds", suspended - advanced))
    minimum_excluded = max(1.0, requested_suspend - 1.0)
    if suspended < minimum_excluded:
        raise SystemExit(f"cycle {index} was not suspended long enough: {report}")
    if advanced < 0.0 or excluded < minimum_excluded:
        raise SystemExit(f"cycle {index} did not exclude suspended time: {report}")
    if cycle.get("runtime_status_after_resume") != "Running":
        raise SystemExit(f"runtime did not resume in cycle {index}: {report}")
if int(report["final_sequence"]) <= int(report["initial_sequence"]):
    raise SystemExit(f"simulation did not progress between lifecycle transitions: {report}")
print(json.dumps(report, indent=2, sort_keys=True))
PY

capture_diagnostics
if [[ ! -s "${REPORT_DIR}/device-screen.png" ]]; then
    printf '%s\n' 'RMA-022 physical-device screenshot is empty.' >&2
    exit 1
fi

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" > "${REPORT_DIR}/force-stop.txt"
shutdown_deadline=$(( $(date +%s) + 20 ))
while "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; do
    if (( $(date +%s) >= shutdown_deadline )); then
        printf '%s\n' 'Unity process remained alive after controlled force-stop.' >&2
        exit 1
    fi
    sleep 1
done
printf 'process_shutdown_verified=true\n' > "${REPORT_DIR}/process-shutdown.txt"

printf 'RMA-022 Unity IL2CPP native lifecycle acceptance passed on %s.\n' \
    "${DEVICE_SERIAL}"

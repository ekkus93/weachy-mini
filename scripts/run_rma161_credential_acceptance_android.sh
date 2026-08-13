#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA161_CREDENTIAL_REPORT_DIR:-${ROOT_DIR}/build/rma161-credential-report}"
PACKAGE_NAME="com.ekkus.weachymini"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
RESULT_FILE="rma161-credential-state.json"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT="${REMOTE_FILES_DIR}/${RESULT_FILE}"
REMOTE_UI_DUMP="/sdcard/rma161-window.xml"
TIMEOUT_SECONDS="${RMA161_CREDENTIAL_TIMEOUT_SECONDS:-120}"
POLL_SECONDS="${RMA161_CREDENTIAL_POLL_SECONDS:-0.5}"

SECRET_MARKERS=(
    "rma161-physical-secret-initial-7f20b3"
    "rma161-physical-secret-updated-9d51c4"
    "rma161-physical-secret-replacement-a31e75"
    "rma161-physical-secret-provider-delete-1c8b42"
    "rma161-physical-secret-app-clear-e4d907"
)

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! "${TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( TIMEOUT_SECONDS <= 0 )); then
    printf 'RMA-161 timeout must be a positive integer: %s\n' \
        "${TIMEOUT_SECONDS}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null
command -v python3 >/dev/null
command -v sha256sum >/dev/null
if [[ ! -s "${FOREGROUND_HELPER}" ]]; then
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
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
        local abi sdk
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
        sdk="$("${ADB_BIN}" -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\r')"
        if [[ "${abi}" == "arm64-v8a" && "${sdk}" =~ ^[0-9]+$ ]] && (( sdk >= 26 )); then
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

capture_failure_diagnostics()
{
    set +e
    "${ADB[@]}" logcat -d -v threadtime > "${REPORT_DIR}/failure-logcat.txt"
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
        > "${REPORT_DIR}/failure-package.txt"
    "${ADB[@]}" shell dumpsys activity activities \
        > "${REPORT_DIR}/failure-activity.txt"
    "${ADB[@]}" exec-out screencap -p \
        > "${REPORT_DIR}/failure-screen.png"
}

cleanup()
{
    local exit_code=$?
    trap - EXIT
    if (( exit_code != 0 )); then
        capture_failure_diagnostics
    fi
    set +e
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1
    "${ADB[@]}" shell rm -f "${REMOTE_UI_DUMP}" >/dev/null 2>&1
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10 \
        >/dev/null 2>&1
    exit "${exit_code}"
}
trap cleanup EXIT

verify_installed_apk_matches_artifact()
{
    local expected_sha installed_sha base_apk_path
    local -a base_apk_paths=()

    mapfile -t base_apk_paths < <(
        "${ADB[@]}" shell pm path "${PACKAGE_NAME}" \
            | tr -d '\r' \
            | sed -n 's#^package:\(.*/base\.apk\)$#\1#p'
    )
    if (( ${#base_apk_paths[@]} != 1 )); then
        printf 'Expected exactly one installed base.apk for %s; found %s.\n' \
            "${PACKAGE_NAME}" "${#base_apk_paths[@]}" >&2
        "${ADB[@]}" shell pm path "${PACKAGE_NAME}" >&2 || true
        return 1
    fi
    base_apk_path="${base_apk_paths[0]}"

    expected_sha="$(sha256sum "${APK_PATH}" | awk '{print $1}')"
    if ! installed_sha="$(
        "${ADB[@]}" exec-out cat "${base_apk_path}" \
            | sha256sum \
            | awk '{print $1}'
    )"; then
        printf 'Could not hash installed base.apk for %s.\n' "${PACKAGE_NAME}" >&2
        return 1
    fi

    if [[ ! "${expected_sha}" =~ ^[0-9a-f]{64}$ \
        || ! "${installed_sha}" =~ ^[0-9a-f]{64}$ ]]; then
        printf 'RMA-161 APK provenance produced an invalid SHA-256 value.\n' >&2
        return 1
    fi
    if [[ "${installed_sha}" != "${expected_sha}" ]]; then
        printf 'Installed APK does not match the exact upstream validated artifact.\n' >&2
        printf 'Expected SHA-256: %s\nInstalled SHA-256: %s\n' \
            "${expected_sha}" "${installed_sha}" >&2
        return 1
    fi

    {
        printf 'artifact_sha256=%s\n' "${expected_sha}"
        printf 'installed_base_apk_sha256=%s\n' "${installed_sha}"
        printf 'installed_base_apk_path=%s\n' "${base_apk_path}"
        printf 'reinstall_skipped=true\n'
    } > "${REPORT_DIR}/installed-apk-provenance.txt"
}

keyguard_state()
{
    "${ADB[@]}" shell dumpsys activity activities 2>/dev/null \
        | tr -d '\r' \
        | awk '
            /KeyguardController:/ { in_keyguard = 1; next }
            in_keyguard && /mKeyguardShowing=/ {
                sub(/^[[:space:]]+/, "")
                showing = $0
                next
            }
            in_keyguard && /mOccluded=/ {
                sub(/^[[:space:]]+/, "")
                occluded = $0
                exit
            }
            END {
                if (showing != "") print showing
                if (occluded != "") print occluded
            }
        '
}

wait_for_keyguard_showing()
{
    local deadline=$(( $(date +%s) + 20 ))
    while true; do
        local state
        state="$(keyguard_state || true)"
        if [[ "${state}" == *"mKeyguardShowing=true"* ]]; then
            printf '%s\n' "${state}"
            return 0
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Device did not enter an observable keyguard-locked state. Last state: %s\n' \
                "${state}" >&2
            return 1
        fi
        sleep 1
    done
}

wait_for_keyguard_dismissed()
{
    local deadline=$(( $(date +%s) + 30 ))
    while true; do
        local state
        state="$(keyguard_state || true)"
        if [[ "${state}" != *"mKeyguardShowing=true"* ]]; then
            printf '%s\n' "${state}"
            return 0
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Device keyguard remained active. Last state: %s\n' "${state}" >&2
            return 1
        fi
        sleep 1
    done
}

validate_phase_report()
{
    local phase="$1"
    local path="$2"
    python3 - "${phase}" "${path}" <<'PY'
from __future__ import annotations

import json
import pathlib
import sys

phase = sys.argv[1]
path = pathlib.Path(sys.argv[2])
report = json.loads(path.read_text(encoding="utf-8"))
if report.get("status") != "passed" or report.get("phase") != phase:
    raise SystemExit(f"RMA-161 phase {phase!r} failed: {report}")
if report.get("full_secret_in_report") is not False:
    raise SystemExit(f"RMA-161 phase {phase!r} reported secret disclosure: {report}")
required = {
    "prepare": ("credential_round_trip", "key_present"),
    "verify-after-lock": ("credential_round_trip", "lock_transition_verified"),
    "invalidate": (
        "invalidation_triggered",
        "read_failed_closed_after_invalidation",
        "update_failed_closed_after_invalidation",
        "encrypted_record_retained_after_invalidation",
        "explicit_delete_succeeded",
        "replacement_key_created",
        "provider_delete_removed_credential",
        "app_clear_credential_prepared",
    ),
    "verify-cleared": (
        "app_data_clear_removed_credential",
        "post_clear_create_read_succeeded",
    ),
}[phase]
for field in required:
    if report.get(field) is not True:
        raise SystemExit(
            f"RMA-161 phase {phase!r} missing required true field {field!r}: {report}"
        )
PY
}

capture_visible_evidence()
{
    local phase="$1"
    "${ADB[@]}" logcat -d -v threadtime > "${REPORT_DIR}/logcat-${phase}.txt"
    "${ADB[@]}" exec-out screencap -p > "${REPORT_DIR}/screen-${phase}.png"
    "${ADB[@]}" shell rm -f "${REMOTE_UI_DUMP}"
    "${ADB[@]}" shell uiautomator dump "${REMOTE_UI_DUMP}" \
        > "${REPORT_DIR}/uiautomator-${phase}.txt"
    "${ADB[@]}" pull "${REMOTE_UI_DUMP}" \
        "${REPORT_DIR}/window-${phase}.xml" >/dev/null
}

run_phase()
{
    local phase="$1"
    "${ADB[@]}" shell mkdir -p "${REMOTE_FILES_DIR}"
    "${ADB[@]}" shell rm -f "${REMOTE_RESULT}" "${REMOTE_RESULT}.tmp"
    "${ADB[@]}" logcat -c
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20 \
        > "${REPORT_DIR}/prepare-device-${phase}.txt"
    "${ADB[@]}" shell am start -W \
        -n "${LAUNCH_COMPONENT}" \
        -a android.intent.action.MAIN \
        -c android.intent.category.LAUNCHER \
        --es reachy_rma161_acceptance_phase "${phase}" \
        > "${REPORT_DIR}/launch-${phase}.txt"
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
        wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30 \
        > "${REPORT_DIR}/focus-${phase}.txt"

    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    local report_json=""
    while true; do
        report_json="$(
            "${ADB[@]}" shell \
                "if test -f '${REMOTE_RESULT}'; then cat '${REMOTE_RESULT}'; fi" \
                2>/dev/null \
                | tr -d '\r' \
        )"
        if [[ -n "${report_json}" ]]; then
            break
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Timed out waiting for RMA-161 phase %s evidence.\n' "${phase}" >&2
            return 1
        fi
        if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
            printf 'Unity application exited before RMA-161 phase %s evidence.\n' \
                "${phase}" >&2
            return 1
        fi
        sleep "${POLL_SECONDS}"
    done

    printf '%s\n' "${report_json}" > "${REPORT_DIR}/${phase}.json"
    validate_phase_report "${phase}" "${REPORT_DIR}/${phase}.json"
    capture_visible_evidence "${phase}"
}

assert_no_full_secret_in_text_evidence()
{
    local marker
    for marker in "${SECRET_MARKERS[@]}"; do
        if grep -R --binary-files=without-match --fixed-strings --quiet \
            -- "${marker}" "${REPORT_DIR}"; then
            printf 'RMA-161 full credential marker leaked into text evidence.\n' >&2
            return 1
        fi
    done
}

verify_installed_apk_matches_artifact
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/initial-pm-clear.txt"
if ! grep -Fxq 'Success' "${REPORT_DIR}/initial-pm-clear.txt"; then
    printf 'Initial RMA-161 app-data clear did not succeed.\n' >&2
    exit 1
fi

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
if ! "${ADB[@]}" shell run-as "${PACKAGE_NAME}" id \
    > "${REPORT_DIR}/debuggable-run-as.txt" 2>&1; then
    printf 'RMA-161 physical acceptance requires the debuggable device-feasibility APK.\n' >&2
    exit 1
fi

run_phase prepare

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
"${ADB[@]}" shell input keyevent 223
sleep 2
wait_for_keyguard_showing > "${REPORT_DIR}/keyguard-locked.txt"
"${ADB[@]}" shell input keyevent 224
sleep 1
wait_for_keyguard_showing > "${REPORT_DIR}/keyguard-woken-locked.txt"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}" \
    prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30 \
    > "${REPORT_DIR}/unlock-prepare.txt"
wait_for_keyguard_dismissed > "${REPORT_DIR}/keyguard-unlocked.txt"

run_phase verify-after-lock
run_phase invalidate

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/pm-clear.txt"
if ! grep -Fxq 'Success' "${REPORT_DIR}/pm-clear.txt"; then
    printf 'RMA-161 app-data clear did not succeed.\n' >&2
    exit 1
fi
run_phase verify-cleared

assert_no_full_secret_in_text_evidence
"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" > "${REPORT_DIR}/package.txt"
"${ADB[@]}" shell dumpsys activity activities > "${REPORT_DIR}/activity.txt"
sha256sum "${APK_PATH}" > "${REPORT_DIR}/apk.sha256"
sha256sum "${REPORT_DIR}"/*.json > "${REPORT_DIR}/reports.sha256"
printf 'device_serial=%s\n' "${DEVICE_SERIAL}" > "${REPORT_DIR}/environment.txt"
printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')" \
    >> "${REPORT_DIR}/environment.txt"
printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')" \
    >> "${REPORT_DIR}/environment.txt"
printf 'RMA-161 physical Android credential lifecycle acceptance passed.\n'

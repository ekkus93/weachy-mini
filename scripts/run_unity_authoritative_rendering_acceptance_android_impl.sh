#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${UNITY_AUTHORITATIVE_REPORT_DIR:-${ROOT_DIR}/build/unity-authoritative-device-report}"
PACKAGE_NAME="com.ekkus.weachymini"
LAUNCH_EXTRA_NAME="weachy_physical_acceptance"
RESULT_FILE_NAME="weachy-authoritative-acceptance.json"
REMOTE_RESULT_PATH="/sdcard/Android/data/${PACKAGE_NAME}/files/${RESULT_FILE_NAME}"
TIMEOUT_SECONDS="${UNITY_AUTHORITATIVE_TIMEOUT_SECONDS:-120}"
INSTALL_TIMEOUT_SECONDS="${UNITY_AUTHORITATIVE_INSTALL_TIMEOUT_SECONDS:-180}"
LAUNCH_READY_FILE="${REPORT_DIR}/launch-issued"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! "${INSTALL_TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]] || (( INSTALL_TIMEOUT_SECONDS <= 0 )); then
    printf 'Authoritative install timeout must be a positive integer: %s\n' \
        "${INSTALL_TIMEOUT_SECONDS}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null
command -v timeout >/dev/null
command -v sha256sum >/dev/null

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

read_report_status()
{
    local report_json="$1"
    python3 - "${report_json}" <<'PY'
from __future__ import annotations

import json
import sys

try:
    report = json.loads(sys.argv[1])
except json.JSONDecodeError:
    print("invalid")
else:
    print(report.get("status", "missing"))
PY
}

resolve_android_build_tool()
{
    local tool_name="$1"
    if command -v "${tool_name}" >/dev/null 2>&1; then
        command -v "${tool_name}"
        return
    fi

    local sdk_root="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
    if [[ -z "${sdk_root}" || ! -d "${sdk_root}/build-tools" ]]; then
        return 1
    fi

    find "${sdk_root}/build-tools" \
        -mindepth 2 \
        -maxdepth 2 \
        -type f \
        -name "${tool_name}" \
        -perm -u+x \
        -print \
        | sort -V \
        | tail -n 1
}

DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:-$(select_device_serial | tail -n 1)}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")
rm -rf -- "${REPORT_DIR}"
mkdir -p "${REPORT_DIR}"

capture_host_apk_evidence()
{
    sha256sum "${APK_PATH}" > "${REPORT_DIR}/apk-sha256.txt"

    local aapt_bin=""
    local apksigner_bin=""
    aapt_bin="$(resolve_android_build_tool aapt || true)"
    apksigner_bin="$(resolve_android_build_tool apksigner || true)"

    if [[ -n "${aapt_bin}" ]]; then
        set +e
        "${aapt_bin}" dump badging "${APK_PATH}" \
            > "${REPORT_DIR}/apk-badging.txt" 2>&1
        printf '%s\n' "$?" > "${REPORT_DIR}/apk-badging-status.txt"
        set -e
    else
        printf '%s\n' 'aapt was not available in PATH or the Android SDK build-tools directories.' \
            > "${REPORT_DIR}/apk-badging.txt"
        printf '%s\n' 'unavailable' > "${REPORT_DIR}/apk-badging-status.txt"
    fi

    if [[ -n "${apksigner_bin}" ]]; then
        set +e
        "${apksigner_bin}" verify --verbose --print-certs "${APK_PATH}" \
            > "${REPORT_DIR}/apk-signature.txt" 2>&1
        printf '%s\n' "$?" > "${REPORT_DIR}/apk-signature-status.txt"
        set -e
    else
        printf '%s\n' 'apksigner was not available in PATH or the Android SDK build-tools directories.' \
            > "${REPORT_DIR}/apk-signature.txt"
        printf '%s\n' 'unavailable' > "${REPORT_DIR}/apk-signature-status.txt"
    fi
}

capture_diagnostics()
{
    set +e
    "${ADB[@]}" logcat -d -v raw > "${REPORT_DIR}/logcat.txt" 2>&1
    "${ADB[@]}" shell dumpsys activity activities \
        > "${REPORT_DIR}/activity.txt" 2>&1
    "${ADB[@]}" shell dumpsys window windows \
        > "${REPORT_DIR}/window.txt" 2>&1
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
        > "${REPORT_DIR}/package.txt" 2>&1
    "${ADB[@]}" shell ps \
        > "${REPORT_DIR}/processes.txt" 2>&1
    "${ADB[@]}" shell \
        "ls -laR '/sdcard/Android/data/${PACKAGE_NAME}' 2>&1" \
        > "${REPORT_DIR}/external-files.txt" 2>&1
    "${ADB[@]}" shell \
        "run-as '${PACKAGE_NAME}' sh -c 'pwd; find . -maxdepth 3 -type f -print' 2>&1" \
        > "${REPORT_DIR}/internal-files.txt" 2>&1
    "${ADB[@]}" exec-out screencap -p \
        > "${REPORT_DIR}/device-screen.png" \
        2> "${REPORT_DIR}/device-screen-error.txt"
}

capture_install_diagnostics()
{
    set +e
    "${ADB[@]}" get-state > "${REPORT_DIR}/adb-state.txt" 2>&1
    "${ADB_BIN}" devices -l > "${REPORT_DIR}/adb-devices.txt" 2>&1
    "${ADB[@]}" shell getprop > "${REPORT_DIR}/getprop.txt" 2>&1
    "${ADB[@]}" shell df -h /data > "${REPORT_DIR}/data-filesystem.txt" 2>&1
    "${ADB[@]}" shell pm path "${PACKAGE_NAME}" \
        > "${REPORT_DIR}/package-path-on-install-failure.txt" 2>&1
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
        > "${REPORT_DIR}/package-on-install-failure.txt" 2>&1
    "${ADB[@]}" shell dumpsys package installer \
        > "${REPORT_DIR}/package-installer.txt" 2>&1
    "${ADB[@]}" logcat -d -b all \
        > "${REPORT_DIR}/install-logcat.txt" 2>&1
}

on_exit()
{
    local exit_code=$?
    trap - EXIT
    if (( exit_code != 0 )); then
        capture_diagnostics
    fi
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

capture_host_apk_evidence

{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'manufacturer=%s\n' "$("${ADB[@]}" shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' "$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
    android_release="$("${ADB[@]}" shell getprop ro.build.version.release | tr -d '\r')"
    printf 'android_release=%s\n' "${android_release}"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
} > "${REPORT_DIR}/device.txt"

candidate_sha256="$(awk 'NR == 1 {print $1}' "${REPORT_DIR}/apk-sha256.txt")"
if [[ ! "${candidate_sha256}" =~ ^[0-9a-f]{64}$ ]]; then
    printf 'Candidate APK digest is invalid: %s\n' "${candidate_sha256}" >&2
    exit 1
fi
printf '%s\n' "${candidate_sha256}" > "${REPORT_DIR}/candidate-apk-sha256.txt"

probe_installed_package()
{
    local label="$1"
    local output="${REPORT_DIR}/package-path-${label}.txt"
    local status_output="${REPORT_DIR}/package-path-${label}-status.txt"
    local selected_output="${REPORT_DIR}/installed-apk-${label}-path.txt"
    local state_output="${REPORT_DIR}/package-path-${label}-adb-state.txt"
    local state_status_output="${REPORT_DIR}/package-path-${label}-adb-state-status.txt"
    local status

    set +e
    "${ADB[@]}" shell pm path "${PACKAGE_NAME}" > "${output}" 2>&1
    status=$?
    set -e
    printf '%s\n' "${status}" > "${status_output}"

    if (( status == 0 )); then
        local -a package_paths=()
        mapfile -t package_paths < <(
            sed -n 's/^package://p' "${output}" | tr -d '\r'
        )
        if (( ${#package_paths[@]} == 0 )); then
            capture_install_diagnostics
            printf '%s\n' \
                'Package Manager reported success without an installed APK path.' >&2
            exit 1
        fi

        local selected_path=""
        if (( ${#package_paths[@]} == 1 )); then
            selected_path="${package_paths[0]}"
        else
            local -a base_paths=()
            local package_path
            for package_path in "${package_paths[@]}"; do
                if [[ "${package_path}" == */base.apk ]]; then
                    base_paths+=("${package_path}")
                fi
            done
            if (( ${#base_paths[@]} != 1 )); then
                capture_install_diagnostics
                printf 'Expected one installed base APK; found %s package paths and %s base paths.\n' \
                    "${#package_paths[@]}" "${#base_paths[@]}" >&2
                exit 1
            fi
            selected_path="${base_paths[0]}"
        fi
        if [[ -z "${selected_path}" ]]; then
            capture_install_diagnostics
            printf '%s\n' 'Installed APK path was empty.' >&2
            exit 1
        fi
        printf '%s\n' "${selected_path}" > "${selected_output}"
        return 0
    fi

    if (( status != 1 )) || [[ -s "${output}" ]]; then
        capture_install_diagnostics
        printf 'Installed-package probe failed with status %s.\n' "${status}" >&2
        exit 1
    fi

    local state_status
    set +e
    "${ADB[@]}" get-state > "${state_output}" 2>&1
    state_status=$?
    set -e
    printf '%s\n' "${state_status}" > "${state_status_output}"
    local state
    state="$(tr -d '\r\n' < "${state_output}")"
    if (( state_status != 0 )) || [[ "${state}" != "device" ]]; then
        capture_install_diagnostics
        printf 'ADB transport was not healthy while confirming package absence: %s.\n' \
            "${state:-missing}" >&2
        exit 1
    fi
    printf '%s\n' 'package_absent=true' \
        > "${REPORT_DIR}/package-${label}-absence.txt"
    return 1
}

hash_installed_apk()
{
    local label="$1"
    local package_path="$2"
    local pulled_apk="${REPORT_DIR}/installed-apk-${label}.apk"
    local pull_output="${REPORT_DIR}/installed-apk-${label}-pull.txt"
    local pull_status_output="${REPORT_DIR}/installed-apk-${label}-pull-status.txt"
    local digest_output="${REPORT_DIR}/installed-apk-${label}-sha256.txt"
    local pull_status

    set +e
    timeout --signal=TERM --kill-after=15s "${INSTALL_TIMEOUT_SECONDS}s" \
        "${ADB[@]}" pull "${package_path}" "${pulled_apk}" \
        > "${pull_output}" 2>&1
    pull_status=$?
    set -e
    printf '%s\n' "${pull_status}" > "${pull_status_output}"
    if (( pull_status != 0 )) || [[ ! -s "${pulled_apk}" ]]; then
        capture_install_diagnostics
        printf 'Installed APK capture failed with status %s.\n' "${pull_status}" >&2
        exit 1
    fi

    sha256sum "${pulled_apk}" > "${digest_output}"
    local digest
    digest="$(awk 'NR == 1 {print $1}' "${digest_output}")"
    rm -f -- "${pulled_apk}"
    if [[ ! "${digest}" =~ ^[0-9a-f]{64}$ ]]; then
        capture_install_diagnostics
        printf 'Installed APK digest is invalid: %s\n' "${digest}" >&2
        exit 1
    fi
    printf '%s' "${digest}"
}

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/force-stop-before-install.txt" 2>&1 || true

install_required=true
install_mode="install_absent"
if probe_installed_package before-install; then
    installed_before_path="$(
        cat "${REPORT_DIR}/installed-apk-before-install-path.txt"
    )"
    installed_before_sha256="$(
        hash_installed_apk before-install "${installed_before_path}"
    )"
    printf '%s\n' "${installed_before_sha256}" \
        > "${REPORT_DIR}/installed-apk-before-install-digest.txt"
    if [[ "${installed_before_sha256}" == "${candidate_sha256}" ]]; then
        install_required=false
        install_mode="reuse_exact_installed_apk"
    else
        install_mode="replace_mismatched_installed_apk"
    fi
fi
printf '%s\n' "${install_mode}" > "${REPORT_DIR}/install-mode.txt"

if [[ "${install_required}" == true ]]; then
    # Replacement mode is intentional. Earlier physical acceptance gates prove
    # this path on the same pinned Android device, while a destructive uninstall
    # followed by a fresh streaming install wedges Package Manager on Android 8.
    set +e
    timeout --signal=TERM --kill-after=15s "${INSTALL_TIMEOUT_SECONDS}s" \
        "${ADB[@]}" install -r -g "${APK_PATH}" \
        > "${REPORT_DIR}/install.txt" 2>&1
    install_status=$?
    set -e
    cat "${REPORT_DIR}/install.txt"
    printf '%s\n' "${install_status}" > "${REPORT_DIR}/install-status.txt"
    if (( install_status != 0 )); then
        capture_install_diagnostics
        if (( install_status == 124 || install_status == 137 )); then
            printf 'APK replacement installation timed out after %s seconds.\n' \
                "${INSTALL_TIMEOUT_SECONDS}" >&2
        else
            printf 'APK replacement installation failed with status %s.\n' \
                "${install_status}" >&2
        fi
        exit "${install_status}"
    fi
else
    printf '%s\n' \
        'reused_exact_installed_apk=true' \
        > "${REPORT_DIR}/install.txt"
    printf '%s\n' '0' > "${REPORT_DIR}/install-status.txt"
fi

if ! probe_installed_package final; then
    capture_install_diagnostics
    printf '%s\n' 'Package was absent after authoritative install selection.' >&2
    exit 1
fi
installed_final_path="$(cat "${REPORT_DIR}/installed-apk-final-path.txt")"
installed_final_sha256="$(hash_installed_apk final "${installed_final_path}")"
printf '%s\n' "${installed_final_sha256}" \
    > "${REPORT_DIR}/installed-apk-final-digest.txt"
if [[ "${installed_final_sha256}" != "${candidate_sha256}" ]]; then
    capture_install_diagnostics
    printf 'Installed APK digest does not match authoritative candidate: %s != %s.\n' \
        "${installed_final_sha256}" "${candidate_sha256}" >&2
    exit 1
fi
printf '%s\n' 'installed_apk_matches_candidate=true' \
    > "${REPORT_DIR}/installed-apk-verified.txt"

"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/package-before-launch.txt" 2>&1
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

"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/force-stop-before-launch.txt" 2>&1
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/clear.txt" 2>&1
"${ADB[@]}" shell rm -f "${REMOTE_RESULT_PATH}" \
    > "${REPORT_DIR}/remove-old-report.txt" 2>&1 || true
"${ADB[@]}" logcat -c > "${REPORT_DIR}/logcat-clear.txt" 2>&1 || true

set +e
"${ADB[@]}" shell am start -W \
    -n "${launch_component}" \
    -a android.intent.action.MAIN \
    -c android.intent.category.LAUNCHER \
    --ez "${LAUNCH_EXTRA_NAME}" true \
    2>&1 | tee "${REPORT_DIR}/launch.txt"
launch_status=${PIPESTATUS[0]}
set -e
printf '%s\n' "${launch_status}" > "${REPORT_DIR}/launch-status.txt"
if (( launch_status != 0 )) || \
        grep -Eq '(^|[[:space:]])(Error:|Exception)' "${REPORT_DIR}/launch.txt"; then
    printf 'Unity launch failed with status %s.\n' "${launch_status}" >&2
    exit 1
fi

launch_ready_tmp="${LAUNCH_READY_FILE}.tmp"
{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'component=%s\n' "${launch_component}"
    printf 'issued_epoch=%s\n' "$(date +%s)"
} > "${launch_ready_tmp}"
mv -f -- "${launch_ready_tmp}" "${LAUNCH_READY_FILE}"

start_epoch="$(date +%s)"
report_json=""
last_report_json=""
while true; do
    report_json="$(read_device_report)"
    if [[ -n "${report_json}" ]]; then
        last_report_json="${report_json}"
        printf '%s\n' "${report_json}" \
            > "${REPORT_DIR}/authoritative-rendering-latest.json"
        report_status="$(read_report_status "${report_json}")"
        case "${report_status}" in
            ok)
                break
                ;;
            failed|failed_acceptance_condition)
                printf '%s\n' "${report_json}" \
                    > "${REPORT_DIR}/authoritative-rendering.json"
                printf 'Authoritative rendering acceptance failed: %s\n' \
                    "${report_json}" >&2
                exit 1
                ;;
            in_progress)
                ;;
            *)
                printf 'Invalid physical acceptance report: %s\n' \
                    "${report_json}" >&2
                exit 1
                ;;
        esac
    fi
    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf '%s\n' 'Unity application exited before authoritative acceptance completed.' >&2
        exit 1
    fi
    now_epoch="$(date +%s)"
    if (( now_epoch - start_epoch >= TIMEOUT_SECONDS )); then
        if [[ -n "${last_report_json}" ]]; then
            printf 'Timed out after %s seconds at report: %s\n' \
                "${TIMEOUT_SECONDS}" "${last_report_json}" >&2
        else
            printf 'Timed out after %s seconds before the application published acceptance evidence.\n' \
                "${TIMEOUT_SECONDS}" >&2
        fi
        exit 1
    fi
    sleep 2
done

printf '%s\n' "${report_json}" > "${REPORT_DIR}/authoritative-rendering.json"
python3 - "${REPORT_DIR}/authoritative-rendering.json" <<'PY'
from __future__ import annotations

import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
report = json.loads(path.read_text(encoding="utf-8"))
expected_true = (
    "body_yaw_moved",
    "head_moved",
    "right_antenna_moved",
    "left_antenna_moved",
    "renderer_structure_valid",
)
if report.get("status") != "ok":
    raise SystemExit(f"acceptance status is not ok: {report}")
if report.get("body_count") != 18:
    raise SystemExit(f"unexpected body count: {report}")
if not str(report.get("model_hash", "")).isdigit() or int(report["model_hash"]) == 0:
    raise SystemExit(f"invalid model hash: {report}")
if report.get("moved_body_count", 0) < 10:
    raise SystemExit(f"insufficient rendered body motion: {report}")
if report.get("moved_stewart_link_count") != 6:
    raise SystemExit(f"not all Stewart links moved: {report}")
for key in expected_true:
    if report.get(key) is not True:
        raise SystemExit(f"required acceptance flag {key} is false: {report}")
if report.get("hidden_kinematic_fallback") is not False:
    raise SystemExit(f"hidden fallback was reported: {report}")
if report.get("renderer_status") != "Rendering":
    raise SystemExit(f"renderer did not remain authoritative: {report}")
if report.get("runtime_status") != "Running":
    raise SystemExit(f"runtime did not remain running: {report}")
if report.get("reset_continuity_id") == report.get("initial_continuity_id"):
    raise SystemExit(f"reset did not advance continuity: {report}")
sequences = [
    int(report["initial_sequence"]),
    int(report["pose_a_sequence"]),
    int(report["pose_b_sequence"]),
]
if not sequences[0] < sequences[1] < sequences[2]:
    raise SystemExit(f"motion sequences are not ordered: {report}")
print(json.dumps(report, indent=2, sort_keys=True))
PY

capture_diagnostics
if [[ ! -s "${REPORT_DIR}/device-screen.png" ]]; then
    printf '%s\n' 'Physical-device screenshot is empty.' >&2
    exit 1
fi
printf 'Authoritative Unity rendering acceptance passed on %s.\n' "${DEVICE_SERIAL}"

from __future__ import annotations

from pathlib import Path

ROOT = Path.cwd()
IMPLEMENTATION = ROOT / "scripts/run_unity_authoritative_rendering_acceptance_android_impl.sh"
CONTRACT = ROOT / "managed/ReachyMini.Camera.Tests/Rma111AndroidBridgeSourceContracts.cs"


def replace_range(path: Path, start_marker: str, end_marker: str, replacement: str) -> None:
    source = path.read_text(encoding="utf-8")
    start = source.find(start_marker)
    if start < 0:
        raise SystemExit(f"Start marker not found in {path}: {start_marker!r}")
    if source.find(start_marker, start + 1) >= 0:
        raise SystemExit(f"Start marker is ambiguous in {path}: {start_marker!r}")
    end = source.find(end_marker, start)
    if end < 0:
        raise SystemExit(f"End marker not found in {path}: {end_marker!r}")
    path.write_text(source[:start] + replacement + source[end:], encoding="utf-8")


def replace_once(path: Path, old: str, new: str) -> None:
    source = path.read_text(encoding="utf-8")
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"Expected one target in {path}; found {count}: {old!r}")
    path.write_text(source.replace(old, new), encoding="utf-8")


replace_once(
    IMPLEMENTATION,
    'command -v timeout >/dev/null\n',
    'command -v timeout >/dev/null\ncommand -v sha256sum >/dev/null\n',
)

install_start = '''"${ADB[@]}" shell pm path "${PACKAGE_NAME}" \\
    > "${REPORT_DIR}/package-path-before-uninstall.txt" 2>&1 || true
'''
install_end = '''"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \\
    > "${REPORT_DIR}/package-before-launch.txt" 2>&1
'''

install_replacement = r'''candidate_sha256="$(awk 'NR == 1 {print $1}' "${REPORT_DIR}/apk-sha256.txt")"
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

'''

replace_range(IMPLEMENTATION, install_start, install_end, install_replacement)

contract_start = '''        private static void VerifyAuthoritativeInstallHarness(
'''
contract_end = '''        private static void VerifyPinnedDeviceWorkflow(
'''
contract_replacement = r'''        private static void VerifyAuthoritativeInstallHarness(
            string wrapper,
            string implementation)
        {
            RequireText(
                implementation,
                "probe_installed_package()",
                "installed-package state probe");
            RequireText(
                implementation,
                "hash_installed_apk()",
                "installed APK digest capture");
            RequireText(
                implementation,
                "reuse_exact_installed_apk",
                "exact installed APK reuse mode");
            RequireText(
                implementation,
                "replace_mismatched_installed_apk",
                "mismatched APK replacement mode");
            RequireText(
                implementation,
                "installed_apk_matches_candidate=true",
                "post-selection exact APK identity evidence");
            RequireText(
                implementation,
                "installed-apk-final-sha256.txt",
                "final installed APK digest evidence");
            RequireText(
                implementation,
                "installed_final_sha256 != \"${candidate_sha256}\"",
                "final installed APK digest equality gate");
            RequireText(
                implementation,
                "status != 1",
                "Android Package Manager absence status contract");
            RequireText(
                implementation,
                "ADB transport was not healthy while confirming package absence",
                "package-absence transport verification");
            RequireText(
                implementation,
                "UNITY_AUTHORITATIVE_INSTALL_TIMEOUT_SECONDS",
                "bounded install timeout configuration");
            RequireText(
                implementation,
                "timeout --signal=TERM --kill-after=15s",
                "bounded ADB transfer and installation");
            RequireText(
                implementation,
                "\"${ADB[@]}\" install -r -g \"${APK_PATH}\"",
                "proven bounded replacement install path");
            RequireText(
                implementation,
                "> \"${REPORT_DIR}/install.txt\" 2>&1",
                "direct complete install output capture");
            RequireText(
                implementation,
                "install_status=$?",
                "real bounded install exit status");
            RequireText(
                implementation,
                "install_status == 124 || install_status == 137",
                "explicit TERM/KILL install timeout diagnosis");
            RequireText(
                implementation,
                "capture_install_diagnostics",
                "installation failure evidence capture");
            RequireText(
                implementation,
                "apk-signature.txt",
                "APK signer evidence");
            RequireText(
                implementation,
                "apk-sha256.txt",
                "APK digest evidence");
            RequireText(
                implementation,
                "shell pm clear \"${PACKAGE_NAME}\"",
                "clean application-data boundary before launch");
            RejectExecutableText(
                implementation,
                "uninstall \"${PACKAGE_NAME}\"",
                "destructive uninstall before authoritative acceptance");
            RejectExecutableText(
                implementation,
                "install --no-streaming",
                "hanging non-streaming installation");
            RejectExecutableText(
                implementation,
                "| tee \"${REPORT_DIR}/install.txt\"",
                "live install pipeline that can outlive timeout");

            int launch = RequireAfter(
                implementation,
                "\"${ADB[@]}\" shell am start -W",
                0,
                "authoritative launch command");
            int launchStatus = RequireAfter(
                implementation,
                "launch_status=${PIPESTATUS[0]}",
                launch,
                "authoritative launch status capture");
            _ = RequireAfter(
                implementation,
                "mv -f -- \"${launch_ready_tmp}\" \"${LAUNCH_READY_FILE}\"",
                launchStatus,
                "post-launch readiness publication");

            int readinessWait = RequireAfter(
                wrapper,
                "while [[ ! -s \"${LAUNCH_READY_FILE}\" ]]",
                0,
                "launch readiness wait");
            int processGuard = RequireAfter(
                wrapper,
                "kill -0 \"${implementation_pid}\"",
                readinessWait,
                "early implementation failure guard");
            _ = RequireAfter(
                wrapper,
                "if [[ -s \"${LAUNCH_READY_FILE}\" ]]; then",
                processGuard,
                "launch readiness race recheck");
            _ = RequireAfter(
                wrapper,
                "wait-focus",
                processGuard,
                "foreground wait after launch readiness");
            RequireText(
                wrapper,
                "LAUNCH_READY_TIMEOUT_SECONDS <= INSTALL_TIMEOUT_SECONDS + 20",
                "outer watchdog cannot preempt install evidence");
            RequireText(
                wrapper,
                "kill \"${implementation_pid}\"",
                "background implementation cleanup");
        }

'''

replace_range(CONTRACT, contract_start, contract_end, contract_replacement)
print("RMA-111 authoritative installed-APK reuse repair applied.")

from __future__ import annotations

from pathlib import Path

ROOT = Path.cwd()
IMPLEMENTATION = ROOT / "scripts/run_unity_authoritative_rendering_acceptance_android_impl.sh"
CONTRACT = ROOT / "managed/ReachyMini.Camera.Tests/Rma111AndroidBridgeSourceContracts.cs"


def replace_once(path: Path, old: str, new: str) -> None:
    source = path.read_text(encoding="utf-8")
    count = source.count(old)
    if count != 1:
        raise SystemExit(
            f"Expected exactly one repair target in {path}; found {count}."
        )
    path.write_text(source.replace(old, new), encoding="utf-8")


old_probe = '''set +e
"${ADB[@]}" shell pm path "${PACKAGE_NAME}" \\
    > "${REPORT_DIR}/package-path-after-uninstall.txt" 2>&1
package_absence_status=$?
set -e
printf '%s\\n' "${package_absence_status}" \\
    > "${REPORT_DIR}/package-path-after-uninstall-status.txt"
if (( package_absence_status != 0 )); then
    capture_install_diagnostics
    printf 'Package absence verification failed with status %s.\\n' \\
        "${package_absence_status}" >&2
    exit "${package_absence_status}"
fi
if grep -q '^package:' "${REPORT_DIR}/package-path-after-uninstall.txt"; then
    capture_install_diagnostics
    printf '%s\\n' 'Package remained installed after authoritative acceptance cleanup.' >&2
    exit 1
fi
'''

new_probe = '''set +e
"${ADB[@]}" shell pm path "${PACKAGE_NAME}" \\
    > "${REPORT_DIR}/package-path-after-uninstall.txt" 2>&1
package_absence_status=$?
set -e
printf '%s\\n' "${package_absence_status}" \\
    > "${REPORT_DIR}/package-path-after-uninstall-status.txt"
if (( package_absence_status == 0 )); then
    if grep -q '^package:' "${REPORT_DIR}/package-path-after-uninstall.txt"; then
        capture_install_diagnostics
        printf '%s\\n' \\
            'Package remained installed after authoritative acceptance cleanup.' >&2
        exit 1
    fi
    capture_install_diagnostics
    printf '%s\\n' \\
        'Package Manager reported success without an installed-package path.' >&2
    exit 1
fi
if (( package_absence_status != 1 )) || \\
        [[ -s "${REPORT_DIR}/package-path-after-uninstall.txt" ]]; then
    capture_install_diagnostics
    printf 'Package absence verification failed with status %s.\\n' \\
        "${package_absence_status}" >&2
    exit 1
fi

set +e
"${ADB[@]}" get-state \\
    > "${REPORT_DIR}/package-absence-adb-state.txt" 2>&1
package_absence_state_status=$?
set -e
printf '%s\\n' "${package_absence_state_status}" \\
    > "${REPORT_DIR}/package-absence-adb-state-status.txt"
package_absence_state="$(
    tr -d '\\r\\n' < "${REPORT_DIR}/package-absence-adb-state.txt"
)"
if (( package_absence_state_status != 0 )) || \\
        [[ "${package_absence_state}" != "device" ]]; then
    capture_install_diagnostics
    printf 'ADB transport was not healthy after package absence verification: %s.\\n' \\
        "${package_absence_state:-missing}" >&2
    exit 1
fi
printf '%s\\n' 'package_absent=true' \\
    > "${REPORT_DIR}/package-absence.txt"
'''

replace_once(IMPLEMENTATION, old_probe, new_probe)

old_contract = '''            RequireText(
                implementation,
                "package_absence_status=$?",
                "fail-closed package absence verification");
            RequireText(
                implementation,
                "UNITY_AUTHORITATIVE_INSTALL_TIMEOUT_SECONDS",
'''

new_contract = '''            RequireText(
                implementation,
                "package_absence_status=$?",
                "fail-closed package absence verification");
            RequireText(
                implementation,
                "package_absence_status != 1",
                "Android Package Manager absent-package status contract");
            RequireText(
                implementation,
                "package_absence_state_status=$?",
                "ADB transport verification after package absence");
            RequireText(
                implementation,
                "package-absence-adb-state-status.txt",
                "package-absence transport status evidence");
            RequireText(
                implementation,
                "UNITY_AUTHORITATIVE_INSTALL_TIMEOUT_SECONDS",
'''

replace_once(CONTRACT, old_contract, new_contract)
print("RMA-111 package-absence repair applied.")

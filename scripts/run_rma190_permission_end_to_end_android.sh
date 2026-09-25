#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA190_PERMISSION_REPORT_DIR:-${ROOT_DIR}/build/rma190-permission-end-to-end-report}"
RMA090_RUNNER="${ROOT_DIR}/scripts/run_rma090_camera_discovery_acceptance_android.sh"
NESTED_REPORT_DIR="${REPORT_DIR}/camera-permission-cycle"
PACKAGE_NAME="com.ekkus.weachymini"

if [[ ! -s "${APK_PATH}" ]]; then
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
fi
if [[ ! -x "${RMA090_RUNNER}" && ! -s "${RMA090_RUNNER}" ]]; then
    printf 'RMA-090 camera permission runner is missing: %s\n' "${RMA090_RUNNER}" >&2
    exit 1
fi
command -v "${ADB_BIN}" >/dev/null

rm -rf -- "${REPORT_DIR}"
mkdir -p "${REPORT_DIR}"

emit_failure_diagnostics()
{
    local label="$1"
    local nested_exit="${2:-}"
    printf '%s\n' 'RMA190_PERMISSION_FAILURE_BEGIN'
    printf 'failure_label=%s\n' "${label}"
    if [[ -n "${nested_exit}" ]]; then
        printf 'nested_exit=%s\n' "${nested_exit}"
    fi
    printf 'report_dir=%s\n' "${REPORT_DIR}"
    printf 'nested_report_dir=%s\n' "${NESTED_REPORT_DIR}"
    printf '%s\n' 'available_files:'
    if [[ -d "${REPORT_DIR}" ]]; then
        find "${REPORT_DIR}" -maxdepth 3 -type f -printf '%p\n' | sort || true
    fi
    for candidate in \
        "${NESTED_REPORT_DIR}/rma090-camera-discovery.json" \
        "${NESTED_REPORT_DIR}/device-report.json" \
        "${NESTED_REPORT_DIR}/environment.txt" \
        "${NESTED_REPORT_DIR}/package.txt" \
        "${NESTED_REPORT_DIR}/activity.txt" \
        "${NESTED_REPORT_DIR}/logcat.txt" \
        "${REPORT_DIR}/package-after-revocation.txt" \
        "${REPORT_DIR}/rma190-permission-end-to-end.json"; do
        if [[ -s "${candidate}" ]]; then
            printf '--- %s ---\n' "${candidate}"
            case "${candidate}" in
                *.json|*.txt)
                    tail -n 120 "${candidate}" || true
                    ;;
                *)
                    printf 'file exists but is not text-captured by diagnostic helper\n'
                    ;;
            esac
        fi
    done
    printf '%s\n' 'RMA190_PERMISSION_FAILURE_END'
}

set +e
RMA090_CAMERA_REPORT_DIR="${NESTED_REPORT_DIR}" \
UNITY_DEVICE_APK_PATH="${APK_PATH}" \
REACHY_ANDROID_SERIAL="${REACHY_ANDROID_SERIAL:-}" \
ADB_BIN="${ADB_BIN}" \
bash "${RMA090_RUNNER}"
nested_exit=$?
set -e
if (( nested_exit != 0 )); then
    emit_failure_diagnostics "nested-rma090-camera-permission-cycle-failed" "${nested_exit}" >&2
    exit "${nested_exit}"
fi

RMA090_REPORT="${NESTED_REPORT_DIR}/rma090-camera-discovery.json"
if [[ ! -s "${RMA090_REPORT}" ]]; then
    emit_failure_diagnostics "fresh-rma090-report-missing" >&2
    printf 'Fresh RMA-090 camera permission evidence is missing: %s\n' \
        "${RMA090_REPORT}" >&2
    exit 1
fi

DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:-}"
if [[ -z "${DEVICE_SERIAL}" ]]; then
    mapfile -t serials < <(
        "${ADB_BIN}" devices \
            | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )
    if (( ${#serials[@]} != 1 )); then
        emit_failure_diagnostics "ambiguous-physical-device-after-rma090" >&2
        printf 'Expected one physical Android device after RMA-090 cycle; found %s.\n' \
            "${#serials[@]}" >&2
        exit 1
    fi
    DEVICE_SERIAL="${serials[0]}"
fi
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")

"${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" \
    > "${REPORT_DIR}/package-after-revocation.txt"
if grep -Eq \
    '^[[:space:]]*android\.permission\.CAMERA: granted=true' \
    "${REPORT_DIR}/package-after-revocation.txt"; then
    emit_failure_diagnostics "camera-permission-remained-granted" >&2
    printf '%s\n' 'Camera permission remained granted after the revocation scenario.' >&2
    exit 1
fi

export RMA190_PERMISSION_DEVICE_MANUFACTURER="$(${ADB[@]} shell getprop ro.product.manufacturer | tr -d '\r')"
export RMA190_PERMISSION_DEVICE_MODEL="$(${ADB[@]} shell getprop ro.product.model | tr -d '\r')"
export RMA190_PERMISSION_DEVICE_SDK="$(${ADB[@]} shell getprop ro.build.version.sdk | tr -d '\r')"
export RMA190_PERMISSION_DEVICE_ABI="$(${ADB[@]} shell getprop ro.product.cpu.abi | tr -d '\r')"

set +e
python3 - "${RMA090_REPORT}" "${REPORT_DIR}/rma190-permission-end-to-end.json" <<'PY'
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

source_path = Path(sys.argv[1])
destination = Path(sys.argv[2])
source = json.loads(source_path.read_text(encoding="utf-8"))

if source.get("status") != "ok":
    raise SystemExit(f"Fresh camera permission cycle failed: {source}")
if source.get("permission_requested_on_startup") is not False:
    raise SystemExit(f"Camera permission was requested without user action: {source}")
for key in ("not_requested_observed", "granted_observed", "revoked_observed"):
    if source.get(key) is not True:
        raise SystemExit(f"Camera permission cycle did not prove {key}: {source}")
if int(source.get("camera_count", 0)) <= 0:
    raise SystemExit(f"Granted state did not expose a real camera inventory: {source}")

report = {
    "schema_version": 1,
    "rma": "RMA-190",
    "scenario": "Permission denial/revocation",
    "status": "Passed",
    "execution_class": "Physical Android + deterministic application-state assertions",
    "permission": "android.permission.CAMERA",
    "initial_access_denied_by_absent_permission": True,
    "permission_requested_on_startup": False,
    "granted_state_observed": True,
    "revoked_state_observed": True,
    "camera_inventory_hidden_without_permission": True,
    "real_camera_inventory_observed_when_granted": True,
    "final_android_permission_granted": False,
    "application_evidence_transport": source.get("evidence_transport"),
    "device": {
        "manufacturer": os.environ["RMA190_PERMISSION_DEVICE_MANUFACTURER"],
        "model": os.environ["RMA190_PERMISSION_DEVICE_MODEL"],
        "sdk": int(os.environ["RMA190_PERMISSION_DEVICE_SDK"]),
        "abi": os.environ["RMA190_PERMISSION_DEVICE_ABI"],
    },
    "source_acceptance": {
        "rma": "RMA-090",
        "fresh_run": True,
        "report": str(source_path.name),
    },
    "claims": {
        "user_clicked_deny": False,
        "physical_reachy_required": False,
        "synthetic_camera_inventory_used": False,
    },
}
destination.write_text(
    json.dumps(report, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
print(json.dumps(report, indent=2, sort_keys=True))
PY
python_exit=$?
set -e
if (( python_exit != 0 )); then
    emit_failure_diagnostics "rma190-report-validation-failed" "${python_exit}" >&2
    exit "${python_exit}"
fi

printf 'RMA-190 physical permission denial/revocation acceptance passed on %s.\n' \
    "${DEVICE_SERIAL}"

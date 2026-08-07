#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
PREFLIGHT="${ROOT_DIR}/scripts/run_rma125_speech_device_preflight.sh"
TMP_DIR="$(mktemp -d)"
FAKE_ADB="${TMP_DIR}/adb"
DEVICE_SERIAL="rma125-test-serial"

cleanup()
{
    rm -rf -- "${TMP_DIR}"
}
trap cleanup EXIT

if [[ ! -s "${PREFLIGHT}" ]]; then
    printf 'RMA-125 device preflight is missing: %s\n' "${PREFLIGHT}" >&2
    exit 1
fi
command -v python3 >/dev/null

cat > "${FAKE_ADB}" <<'FAKEADB'
#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" == "-s" ]]; then
    shift 2
fi

case "${1:-}" in
    get-state)
        printf 'device\n'
        ;;
    shell)
        if [[ "${2:-}" != "getprop" ]]; then
            printf 'Unexpected fake adb shell command.\n' >&2
            exit 90
        fi
        case "${3:-}" in
            ro.build.version.sdk) printf '%s\n' "${FAKE_SDK:?}" ;;
            ro.product.cpu.abi) printf '%s\n' "${FAKE_ABI:-arm64-v8a}" ;;
            ro.product.manufacturer) printf 'RMA125Test\n' ;;
            ro.product.model) printf 'SyntheticDevice\n' ;;
            ro.build.version.release) printf '%s\n' "${FAKE_RELEASE:-12}" ;;
            ro.kernel.qemu) printf '%s\n' "${FAKE_QEMU:-}" ;;
            *) printf '\n' ;;
        esac
        ;;
    *)
        printf 'Unexpected fake adb invocation: %s\n' "$*" >&2
        exit 91
        ;;
esac
FAKEADB
chmod +x "${FAKE_ADB}"

run_case()
{
    local name="$1"
    local sdk="$2"
    local abi="$3"
    local qemu="$4"
    local expected_rc="$5"
    local expected_eligibility="$6"
    local report_dir="${TMP_DIR}/${name}"
    local output="${TMP_DIR}/${name}.out"
    local error="${TMP_DIR}/${name}.err"

    set +e
    ADB_BIN="${FAKE_ADB}" \
        REACHY_ANDROID_SERIAL="${DEVICE_SERIAL}" \
        FAKE_SDK="${sdk}" \
        FAKE_ABI="${abi}" \
        FAKE_QEMU="${qemu}" \
        RMA125_SPEECH_PREFLIGHT_REPORT_DIR="${report_dir}" \
        bash "${PREFLIGHT}" >"${output}" 2>"${error}"
    local actual_rc=$?
    set -e

    if (( actual_rc != expected_rc )); then
        printf '%s: expected exit %s, got %s.\n' \
            "${name}" "${expected_rc}" "${actual_rc}" >&2
        cat "${output}" >&2
        cat "${error}" >&2
        exit 1
    fi

    python3 - "${report_dir}/device-preflight.json" \
        "${expected_eligibility}" "${sdk}" "${abi}" <<'PY'
from __future__ import annotations

import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
expected_eligibility = sys.argv[2]
expected_sdk = int(sys.argv[3])
expected_abi = sys.argv[4]
report = json.loads(path.read_text(encoding="utf-8"))

if report.get("schema_version") != 1 or report.get("rma") != "RMA-125":
    raise SystemExit(f"invalid report identity: {report}")
if report.get("eligibility") != expected_eligibility:
    raise SystemExit(f"eligibility mismatch: {report}")
device = report.get("device", {})
if device.get("sdk") != expected_sdk or device.get("abi") != expected_abi:
    raise SystemExit(f"device facts mismatch: {report}")
serial_hash = str(device.get("serial_sha256", ""))
if len(serial_hash) != 64 or any(ch not in "0123456789abcdef" for ch in serial_hash):
    raise SystemExit(f"serial hash is invalid: {report}")
claims = report.get("claims", {})
for name in (
    "proves_on_device_asr_service_available",
    "proves_language_model_installed",
    "proves_offline_tts_voice_installed",
    "proves_network_disabled",
    "proves_rma125_complete",
):
    if claims.get(name) is not False:
        raise SystemExit(f"preflight overclaims {name}: {report}")
requirements = report.get("requirements", {})
if requirements.get("minimum_sdk") != 31:
    raise SystemExit(f"minimum SDK changed unexpectedly: {report}")
if requirements.get("required_abi") != "arm64-v8a":
    raise SystemExit(f"required ABI changed unexpectedly: {report}")
PY
}

run_case api26 26 arm64-v8a '' 2 blocked
run_case api31 31 arm64-v8a '' 0 eligible
run_case emulator31 31 arm64-v8a 1 2 blocked
run_case x86_64_31 31 x86_64 '' 2 blocked

printf 'RMA-125 speech-device preflight contracts passed.\n'

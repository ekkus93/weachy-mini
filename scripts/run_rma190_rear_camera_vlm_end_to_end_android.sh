#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ADB_BIN="${ADB_BIN:-adb}"
APK_PATH="${UNITY_DEVICE_APK_PATH:-${ROOT_DIR}/Builds/Android/weachy-mini-device-arm64-api26.apk}"
REPORT_DIR="${RMA190_REAR_VLM_REPORT_DIR:-${ROOT_DIR}/build/rma190-rear-camera-vlm-end-to-end-report}"
PACKAGE_NAME="com.ekkus.weachymini"
FOREGROUND_HELPER="${ROOT_DIR}/scripts/android_device_acceptance_foreground.sh"
RESULT_FILE="rma190-rear-camera-vlm-end-to-end-acceptance.json"
REMOTE_FILES_DIR="/sdcard/Android/data/${PACKAGE_NAME}/files"
REMOTE_RESULT="${REMOTE_FILES_DIR}/${RESULT_FILE}"
TIMEOUT_SECONDS="${RMA190_REAR_VLM_TIMEOUT_SECONDS:-150}"
POLL_SECONDS="${RMA190_REAR_VLM_POLL_SECONDS:-0.5}"
FIXTURE_PORT="${RMA190_REAR_VLM_FIXTURE_PORT:-18765}"
FIXTURE_META="${REPORT_DIR}/fixture-metadata.json"
FIXTURE_SCRIPT="${REPORT_DIR}/fixture-server.py"
FIXTURE_PID=""
LOG_START=""

[[ -s "${APK_PATH}" ]] || {
    printf 'Unity device APK is missing: %s\n' "${APK_PATH}" >&2
    exit 1
}
command -v "${ADB_BIN}" >/dev/null
command -v python3 >/dev/null
command -v sha256sum >/dev/null
[[ -s "${FOREGROUND_HELPER}" ]] || {
    printf 'Android foreground helper is missing: %s\n' "${FOREGROUND_HELPER}" >&2
    exit 1
}

select_device_serial()
{
    mapfile -t serials < <(
        "${ADB_BIN}" devices |
            awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )
    local -a accepted=()
    local serial
    for serial in "${serials[@]}"; do
        local abi sdk
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\r')"
        sdk="$("${ADB_BIN}" -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\r')"
        if [[ "${abi}" == "arm64-v8a" && "${sdk}" =~ ^[0-9]+$ ]] &&
            (( sdk >= 26 )); then
            accepted+=("${serial}")
        fi
    done
    if (( ${#accepted[@]} != 1 )); then
        printf 'Expected exactly one physical arm64-v8a API-26+ device; found %s.\n'             "${#accepted[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi
    printf '%s\n' "${accepted[0]}"
}

DEVICE_SERIAL="${REACHY_ANDROID_SERIAL:-$(select_device_serial)}"
ADB=("${ADB_BIN}" -s "${DEVICE_SERIAL}")

rm -rf -- "${REPORT_DIR}"
mkdir -p "${REPORT_DIR}"

cat > "${FIXTURE_SCRIPT}" <<'PY'
from __future__ import annotations

import json
import os
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

PORT = int(sys.argv[1])
META = Path(sys.argv[2])
EXPECTED_PROMPT = "What is visible behind Reachy?"
RESPONSE_TEXT = "rma190-rear-camera-fixture-observed"
MAX_BODY = 8 * 1024 * 1024


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args: object) -> None:
        return

    def do_GET(self) -> None:
        if self.path != "/health":
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header("Content-Type", "text/plain")
        self.end_headers()
        self.wfile.write(b"ok\n")

    def do_POST(self) -> None:
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0 or length > MAX_BODY:
            self.send_error(413)
            return
        body = self.rfile.read(length)
        text = body.decode("utf-8", errors="strict")
        has_image = "data:image/jpeg;base64," in text
        has_prompt = EXPECTED_PROMPT in text
        has_authorization = self.headers.get("Authorization", "").startswith(
            "Bearer "
        )
        valid_path = self.path == "/v1/chat/completions"
        try:
            json.loads(text)
            valid_json = True
        except json.JSONDecodeError:
            valid_json = False

        prior_count = 0
        if META.exists():
            try:
                prior_count = int(
                    json.loads(META.read_text(encoding="utf-8")).get(
                        "request_count", 0
                    )
                )
            except (OSError, ValueError, json.JSONDecodeError):
                prior_count = 0

        metadata = {
            "request_count": prior_count + 1,
            "path": self.path,
            "body_bytes": len(body),
            "valid_json": valid_json,
            "contains_jpeg_data_uri": has_image,
            "contains_expected_prompt": has_prompt,
            "authorization_header_present": has_authorization,
            "raw_request_persisted": False,
            "authorization_value_persisted": False,
        }
        tmp = META.with_suffix(".tmp")
        tmp.write_text(
            json.dumps(metadata, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        os.replace(tmp, META)

        if not (
            valid_path
            and valid_json
            and has_image
            and has_prompt
            and has_authorization
        ):
            self.send_error(400)
            return

        payload = {
            "choices": [
                {
                    "message": {
                        "content": RESPONSE_TEXT,
                    }
                }
            ]
        }
        encoded = json.dumps(payload).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)


ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
PY

python3 -u "${FIXTURE_SCRIPT}" "${FIXTURE_PORT}" "${FIXTURE_META}"     > "${REPORT_DIR}/fixture-server.log" 2>&1 &
FIXTURE_PID="$!"
# Full cleanup is installed later; early failures must not orphan this server.
trap 'kill "${FIXTURE_PID}" >/dev/null 2>&1 || true; wait "${FIXTURE_PID}" 2>/dev/null || true' EXIT
sleep 1
if ! kill -0 "${FIXTURE_PID}" 2>/dev/null; then
    printf 'RMA-190 VLM fixture server failed to start.\n' >&2
    cat "${REPORT_DIR}/fixture-server.log" >&2 || true
    exit 1
fi

# A live process is not evidence that its local HTTP endpoint is ready.
# Probe loopback with proxy settings disabled. No frames or credentials here.
python3 - "${FIXTURE_PORT}" <<'PY'
import sys
from urllib.request import ProxyHandler, build_opener

port = int(sys.argv[1])
with build_opener(ProxyHandler({})).open(
    f"http://127.0.0.1:{port}/health", timeout=3
) as response:
    if response.status != 200 or response.read(16) != b"ok\n":
        raise SystemExit("RMA-190 local VLM fixture health check failed.")
PY

read_report()
{
    local value
    value="$("${ADB[@]}" exec-out run-as "${PACKAGE_NAME}" sh -c         "test -f '${REMOTE_RESULT}' && cat '${REMOTE_RESULT}'"         2>/dev/null | tr -d '\r' || true)"
    if [[ -z "${value}" ]]; then
        value="$("${ADB[@]}" shell             "if test -f '${REMOTE_RESULT}'; then cat '${REMOTE_RESULT}'; fi"             2>/dev/null | tr -d '\r' || true)"
    fi
    printf '%s' "${value}"
}

capture_diagnostics()
{
    # This scenario handles private camera frames. Export only app-scoped,
    # bounded, allowlisted events from the current invocation. Never write
    # raw logcat to disk, and never capture the device display or global
    # camera/activity inventories.
    : > "${REPORT_DIR}/logcat.txt"
    : > "${REPORT_DIR}/resource-transitions.txt"
    local app_pid=""
    app_pid="$("${ADB[@]}" shell pidof "${PACKAGE_NAME}" 2>/dev/null | tr -d '\r' | awk '{print $1}' || true)"
    if [[ "${app_pid}" =~ ^[0-9]+$ && -n "${LOG_START:-}" ]]; then
        "${ADB[@]}" logcat -d -v threadtime --pid="${app_pid}" -T "${LOG_START}" 2>/dev/null |
            python3 -c '
import re
import sys
from collections import deque

events = deque(maxlen=48)
transitions = deque(maxlen=24)
statuses = re.compile(r"\b(started|completed|passed|failed|unavailable|faulted|cancelled)\b", re.I)
resource = re.compile(r"RMA-181 sampled resource transition: ([A-Za-z0-9_.,:;=+ /()\-]{1,160})")
for line in sys.stdin:
    if match := resource.search(line):
        transitions.append("RMA-181 sampled resource transition: " + match.group(1).strip())
    elif "RMA-190" in line and (status := statuses.search(line)):
        # Do not persist arbitrary log text, prompts, URLs or camera data.
        events.append("RMA-190 stage: " + status.group(1).lower())
sys.stdout.write("\n".join(events) + ("\n" if events else ""))
with open(sys.argv[1], "w", encoding="utf-8") as out:
    out.write("\n".join(transitions) + ("\n" if transitions else ""))
' "${REPORT_DIR}/resource-transitions.txt" > "${REPORT_DIR}/logcat.txt" || true
    fi
    # Package metadata is scoped to this app and filtered to version fields.
    "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" 2>/dev/null |
        grep -E '^[[:space:]]*(versionCode=|versionName=|minSdk=|targetSdk=)' |
        head -n 12 > "${REPORT_DIR}/package.txt" || true
    read_report > "${REPORT_DIR}/device-report.json" || true
}

cleanup()
{
    local exit_code=$?
    trap - EXIT
    capture_diagnostics
    if (( exit_code != 0 )); then
        printf 'Bounded RMA-181 resource transitions from the physical device:\n' >&2
        tail -n 24 "${REPORT_DIR}/resource-transitions.txt" >&2 || true
        if [[ -s "${FIXTURE_META}" ]]; then
            printf 'Sanitized RMA-190 VLM fixture request metadata:\n' >&2
            python3 - "${FIXTURE_META}" <<'PY'
import json
import sys
from pathlib import Path

metadata = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
safe_fields = (
    "request_count",
    "path",
    "body_bytes",
    "valid_json",
    "contains_jpeg_data_uri",
    "contains_expected_prompt",
    "authorization_header_present",
    "raw_request_persisted",
    "authorization_value_persisted",
)
print(json.dumps({key: metadata.get(key) for key in safe_fields}, sort_keys=True),
      file=sys.stderr)
PY
        else
            printf 'No VLM fixture POST was recorded; inspect adb-reverse.txt and fixture-server.log in the artifact.\n' >&2
        fi
    fi
    "${ADB[@]}" shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
    "${ADB[@]}" reverse --remove "tcp:${FIXTURE_PORT}" >/dev/null 2>&1 || true
    if [[ -n "${FIXTURE_PID}" ]]; then
        kill "${FIXTURE_PID}" >/dev/null 2>&1 || true
        wait "${FIXTURE_PID}" 2>/dev/null || true
    fi
    ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}"         restore "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 10         >/dev/null 2>&1 || true
    exit "${exit_code}"
}
trap cleanup EXIT

"${ADB[@]}" reverse "tcp:${FIXTURE_PORT}" "tcp:${FIXTURE_PORT}"
"${ADB[@]}" reverse --list > "${REPORT_DIR}/adb-reverse.txt"
if ! grep -Fq "tcp:${FIXTURE_PORT} tcp:${FIXTURE_PORT}" "${REPORT_DIR}/adb-reverse.txt"; then
    printf 'RMA-190 expected ADB reverse fixture mapping was not installed.\n' >&2
    exit 1
fi

"${ADB[@]}" install -r -g "${APK_PATH}" > "${REPORT_DIR}/install.txt"
"${ADB[@]}" shell pm clear "${PACKAGE_NAME}" > "${REPORT_DIR}/pm-clear.txt"
"${ADB[@]}" shell pm grant "${PACKAGE_NAME}" android.permission.CAMERA

# The shipping HTTP transport requires Android INTERNET permission even when
# its configured fixture endpoint is loopback and explicitly authorized.
# Check the installed package rather than trusting source manifest merging.
if ! "${ADB[@]}" shell dumpsys package "${PACKAGE_NAME}" |
    grep -F "android.permission.INTERNET: granted=true" >/dev/null; then
    printf 'RMA-190 installed Android APK does not grant INTERNET permission.\n' >&2
    exit 1
fi

LAUNCH_COMPONENT="$(
    "${ADB[@]}" shell cmd package resolve-activity --brief         -a android.intent.action.MAIN         -c android.intent.category.LAUNCHER         "${PACKAGE_NAME}" |
        tr -d '\r' |
        tail -n 1
)"
if [[ -z "${LAUNCH_COMPONENT}" || "${LAUNCH_COMPONENT}" != */* ]]; then
    printf 'Could not resolve launch component for %s: %s\n'         "${PACKAGE_NAME}" "${LAUNCH_COMPONENT}" >&2
    exit 1
fi

"${ADB[@]}" shell mkdir -p "${REMOTE_FILES_DIR}"
"${ADB[@]}" shell rm -f "${REMOTE_RESULT}" "${REMOTE_RESULT}.tmp"
"${ADB[@]}" shell am force-stop "${PACKAGE_NAME}"
# Device-local timestamp, captured before launch; do not clear global logs.
LOG_START="$("${ADB[@]}" shell date '+%m-%d %H:%M:%S.000' 2>/dev/null | tr -d '\r' || true)"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}"     prepare "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 20     > "${REPORT_DIR}/prepare.txt"

FIXTURE_URL="http://127.0.0.1:${FIXTURE_PORT}/"
"${ADB[@]}" shell am start -W     -n "${LAUNCH_COMPONENT}"     -a android.intent.action.MAIN     -c android.intent.category.LAUNCHER     --ez reachy_rma190_rear_camera_vlm_e2e_acceptance true     --es reachy_rma190_rear_camera_vlm_fixture_base_url "${FIXTURE_URL}"     > "${REPORT_DIR}/launch.txt"
cat "${REPORT_DIR}/launch.txt"
ADB_BIN="${ADB_BIN}" bash "${FOREGROUND_HELPER}"     wait-focus "${DEVICE_SERIAL}" "${PACKAGE_NAME}" 30     > "${REPORT_DIR}/focus.txt"

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
report_json=""
while true; do
    report_json="$(read_report)"
    if [[ "${report_json}" == \{* ]]; then
        break
    fi
    report_json=""
    if (( $(date +%s) >= deadline )); then
        printf 'Timed out waiting for RMA-190 rear-camera/VLM evidence.\n' >&2
        exit 1
    fi
    if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
        printf 'Unity exited before rear-camera/VLM evidence was written.\n' >&2
        exit 1
    fi
    sleep "${POLL_SECONDS}"
done

printf '%s\n' "${report_json}" > "${REPORT_DIR}/${RESULT_FILE}"
python3 - "${REPORT_DIR}/${RESULT_FILE}" <<'PY'
from __future__ import annotations

import json
import pathlib
import sys

report = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
assert report["status"] == "passed", report
assert report["real_rear_camera"] is True, report
assert report["transformed_reachy_eye_frame"] is True, report
assert report["deterministic_calibration_fixture"] is True, report
assert report["measured_calibration_claimed"] is False, report
assert report["calibration_provenance"] == "UncalibratedEstimate", report
assert report["camera_id"], report
assert report["camera_session_id"] > 0, report
assert report["source_sequence"] > 0, report
assert report["authoritative_sequence"] > 0, report
assert report["continuity_id"] > 0, report
assert 0.0 < float(report["coverage_fraction"]) <= 1.0, report
assert report["external_vlm_fixture"] is True, report
assert report["fixture_is_live_cloud_evidence"] is False, report
assert report["production_vlm_transport_and_encoder"] is True, report
assert report["synthetic_camera_frame_injected"] is False, report
assert report["raw_camera_payload_persisted"] is False, report
assert report["vlm_response"] == "rma190-rear-camera-fixture-observed", report
print(json.dumps(report, indent=2, sort_keys=True))
PY

python3 - "${FIXTURE_META}" <<'PY'
from __future__ import annotations

import json
import pathlib
import sys

meta = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
assert meta["request_count"] >= 1, meta
assert meta["path"] == "/v1/chat/completions", meta
assert meta["body_bytes"] > 0, meta
assert meta["valid_json"] is True, meta
assert meta["contains_jpeg_data_uri"] is True, meta
assert meta["contains_expected_prompt"] is True, meta
assert meta["authorization_header_present"] is True, meta
assert meta["raw_request_persisted"] is False, meta
assert meta["authorization_value_persisted"] is False, meta
print(json.dumps(meta, indent=2, sort_keys=True))
PY

capture_diagnostics
sha256sum "${APK_PATH}" > "${REPORT_DIR}/apk.sha256"
sha256sum "${REPORT_DIR}/${RESULT_FILE}"     > "${REPORT_DIR}/report.sha256"
{
    printf 'device_serial=%s\n' "${DEVICE_SERIAL}"
    printf 'sdk=%s\n' "$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
    printf 'fixture_transport=adb-reverse-localhost\n'
    printf 'fixture_is_live_cloud_evidence=false\n'
} > "${REPORT_DIR}/environment.txt"

printf '%s\n'     'RMA-190 real rear camera -> transformed frame -> production VLM fixture scenario passed.'

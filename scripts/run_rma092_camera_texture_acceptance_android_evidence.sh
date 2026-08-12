# shellcheck shell=bash
#
# Evidence-polling and JSON-validation helpers for
# run_rma092_camera_texture_acceptance_android.sh.
#
# Split out of that script (docs/LARGE_FILE_REFACTOR_TODO_3.md-style refactor,
# performed after round 3 closed) to keep the entrypoint under 800 lines. This
# file is meant to be `source`d, not executed directly -- it only defines
# functions and relies on globals (ADB, REPORT_DIR, PACKAGE_NAME,
# REMOTE_ACQUISITION_COMMAND, REMOTE_FILES_DIR, REMOTE_TEXTURE_RESULT,
# TIMEOUT_SECONDS, POLL_SECONDS) and on read_remote_file/read_stage_marker/
# pull_remote_evidence (defined in the sibling _device.sh library) that the
# entrypoint defines/sources before any of these functions are actually
# called.

write_command()
{
    local command_id="$1"
    local action="$2"
    local facing="${3:-}"
    local local_path="${REPORT_DIR}/command-${command_id}.json"
    python3 - "${command_id}" "${action}" "${facing}" > "${local_path}" <<'PY'
from __future__ import annotations

import json
import sys

print(json.dumps({
    "id": sys.argv[1],
    "action": sys.argv[2],
    "facing": sys.argv[3],
}, separators=(",", ":")))
PY
    "${ADB[@]}" shell mkdir -p "${REMOTE_FILES_DIR}"
    "${ADB[@]}" push "${local_path}" "${REMOTE_ACQUISITION_COMMAND}.tmp" \
        > "${REPORT_DIR}/push-${command_id}.txt"
    "${ADB[@]}" shell mv \
        "${REMOTE_ACQUISITION_COMMAND}.tmp" \
        "${REMOTE_ACQUISITION_COMMAND}"
}

json_field()
{
    local report_json="$1"
    local path="$2"
    python3 - "${report_json}" "${path}" <<'PY'
from __future__ import annotations

import json
import sys

try:
    value = json.loads(sys.argv[1])
    for component in sys.argv[2].split("."):
        if not isinstance(value, dict):
            value = ""
            break
        value = value.get(component, "")
except (json.JSONDecodeError, TypeError):
    value = ""

if isinstance(value, bool):
    print("true" if value else "false")
elif value is None:
    print("")
else:
    print(value)
PY
}

report_matches()
{
    local report_json="$1"
    local condition="$2"
    shift 2
    python3 - "${report_json}" "${condition}" "$@" <<'PY'
from __future__ import annotations

import json
import sys

try:
    report = json.loads(sys.argv[1])
except json.JSONDecodeError:
    raise SystemExit(1)

condition = sys.argv[2]
args = sys.argv[3:]

def integer(path: str) -> int:
    value = report
    for component in path.split("."):
        if not isinstance(value, dict):
            return 0
        value = value.get(component, 0)
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0

matched = False
if condition == "acquisition_ready":
    matched = (
        report.get("status") == "ok"
        and report.get("acceptance_enabled") is True
        and report.get("permission") == "Granted"
        and report.get("current_state") in {
            "Stopped", "PermissionRevoked", "Unavailable"
        }
    )
elif condition == "acquisition_running":
    command_id, facing = args
    matched = (
        report.get("status") == "ok"
        and report.get("last_command_id") == command_id
        and report.get("last_command_status") == "ok"
        and report.get("current_state") == "Running"
        and report.get("requested_facing") == facing
        and integer("accepted_frame_count") >= 5
        and report.get("metadata_monotonic") is True
        and report.get("all_frames_positive_timestamp") is True
    )
elif condition == "acquisition_stopped":
    command_id = args[0]
    matched = (
        report.get("last_command_id") == command_id
        and report.get("last_command_status") == "ok"
        and report.get("current_state") == "Stopped"
        and "CLOSED" in str(report.get("message", ""))
    )

raise SystemExit(0 if matched else 1)
PY
}

wait_for_report()
{
    local remote_path="$1"
    local description="$2"
    local destination="$3"
    local condition="$4"
    shift 4
    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    local report_json=""
    while true; do
        report_json="$(read_remote_file "${remote_path}")"
        if [[ -n "${report_json}" ]]; then
            if report_matches "${report_json}" "${condition}" "$@"; then
                printf '%s\n' "${report_json}" > "${destination}"
                return
            fi
            local status
            status="$(json_field "${report_json}" status)"
            if [[ "${status}" == "error" ]]; then
                printf 'Evidence faulted while waiting for %s: %s\n' \
                    "${description}" "${report_json}" >&2
                return 1
            fi
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Timed out waiting for %s; last report: %s\n' \
                "${description}" "${report_json:-none}" >&2
            return 1
        fi
        if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
            printf 'Unity application exited while waiting for %s.\n' \
                "${description}" >&2
            return 1
        fi
        sleep "${POLL_SECONDS}"
    done
}

validate_texture_stage()
{
    local report_json="$1"
    local marker_text="$2"
    local expected_facing="$3"
    local capture_field="$4"
    local previous_rotation="${5:-}"
    python3 - "${report_json}" "${marker_text}" \
        "${expected_facing}" "${capture_field}" "${previous_rotation}" <<'PY'
from __future__ import annotations

import json
import re
import sys

report = json.loads(sys.argv[1])
marker_text = sys.argv[2]
expected_facing = sys.argv[3]
capture_field = sys.argv[4]
previous_rotation = sys.argv[5]


def integer(path: str) -> int:
    value = report
    for component in path.split("."):
        if not isinstance(value, dict):
            return 0
        value = value.get(component, 0)
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0

core_true = (
    "descriptor_monotonic",
    "timestamp_correspondence",
    "output_dimensions_valid",
    "mirror_contract_valid",
    "color_contract_valid",
)
if report.get("status") != "ok" or report.get("acceptance_enabled") is not True:
    raise SystemExit(1)
if report.get("bridge_state") != "Ready":
    raise SystemExit(1)
if integer("observed_frame_count") < 3 or integer("metadata_match_count") < 1:
    raise SystemExit(1)
if integer("stale_frame_count") != 0:
    raise SystemExit(1)
if any(report.get(field) is not True for field in core_true):
    raise SystemExit(1)

frame = report.get("frame") or {}
if frame.get("lens_facing") != expected_facing:
    raise SystemExit(1)
if int(frame.get("output_width", 0)) <= 0 or int(frame.get("output_height", 0)) <= 0:
    raise SystemExit(1)
expected_mirror = expected_facing == "Front"
if frame.get("mirrored") is not expected_mirror:
    raise SystemExit(1)
rotation = int(frame.get("rotation_degrees", -1))
if rotation not in {0, 90, 180, 270}:
    raise SystemExit(1)
if previous_rotation and rotation == int(previous_rotation):
    raise SystemExit(1)

# Prefer a real non-uniform live-camera capture whenever the unattended scene
# provides one. This remains the strongest physical evidence path.
if report.get(capture_field) is True:
    if report.get("captures_non_uniform") is not True:
        raise SystemExit(1)
    if report.get("captures_opaque") is not True:
        raise SystemExit(1)
    print(json.dumps({
        "mode": "live_camera_texture",
        "rotation_degrees": rotation,
        "frame_sequence": int(frame.get("sequence", 0)),
        "remote_capture_written": True,
    }, separators=(",", ":")))
    raise SystemExit(0)

# An unattended runner can legitimately point at a covered or dark surface.
# In that case, require all of the following instead of misclassifying correct
# black conversion as a broken GPU path:
#   * deterministic YUV->RGB shader probe passed on this physical GPU/API;
#   * live JNI/Texture2D planes are neutral limited-range black;
#   * the live RGB readback is correspondingly black;
#   * the marker sequence closely tracks the reported live frame.
lines = marker_text.splitlines()
if len(lines) < 2:
    raise SystemExit(1)
filename = lines[0].strip()
detail = " ".join(line.strip() for line in lines[1:] if line.strip())
name_match = re.fullmatch(
    r"rma092-stage-synth-(?P<synth>[01])-rgb-(?P<rgb_min>\d+)-(?P<rgb_max>\d+)-"
    r"opaque-(?P<opaque>[01])-y-(?P<y_min>\d+)-(?P<y_max>\d+)-"
    r"u-(?P<u_min>\d+)-(?P<u_max>\d+)-v-(?P<v_min>\d+)-(?P<v_max>\d+)-"
    r"api-(?P<api>[^/]+)\.txt",
    filename,
)
detail_match = re.search(r"live sequence=(\d+);", detail)
if name_match is None or detail_match is None:
    raise SystemExit(1)
values = {key: int(value) for key, value in name_match.groupdict().items()
          if key != "api"}
marker_sequence = int(detail_match.group(1))
frame_sequence = int(frame.get("sequence", 0))
if values["synth"] != 1 or values["opaque"] != 1:
    raise SystemExit(1)
if values["rgb_max"] - values["rgb_min"] < 128:
    raise SystemExit(1)
if values["y_max"] > 20 or values["y_max"] - values["y_min"] > 8:
    raise SystemExit(1)
for component in ("u_min", "u_max", "v_min", "v_max"):
    if not 96 <= values[component] <= 160:
        raise SystemExit(1)
if abs(frame_sequence - marker_sequence) > 10:
    raise SystemExit(1)
if integer("rejected_capture_count") < 1:
    raise SystemExit(1)
if integer("last_capture_minimum_channel") > 4:
    raise SystemExit(1)
if integer("last_capture_maximum_channel") > 4:
    raise SystemExit(1)

print(json.dumps({
    "mode": "dark_scene_synthetic_gpu_probe",
    "rotation_degrees": rotation,
    "frame_sequence": frame_sequence,
    "marker_sequence": marker_sequence,
    "graphics_api": name_match.group("api"),
    "synthetic_rgb_range": [values["rgb_min"], values["rgb_max"]],
    "live_y_range": [values["y_min"], values["y_max"]],
    "live_u_range": [values["u_min"], values["u_max"]],
    "live_v_range": [values["v_min"], values["v_max"]],
    "remote_capture_written": False,
}, separators=(",", ":")))
PY
}

wait_for_texture_stage()
{
    local description="$1"
    local report_destination="$2"
    local marker_destination="$3"
    local verdict_destination="$4"
    local expected_facing="$5"
    local capture_field="$6"
    local previous_rotation="${7:-}"
    local deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
    local report_json=""
    local marker_text=""
    while true; do
        report_json="$(read_remote_file "${REMOTE_TEXTURE_RESULT}")"
        marker_text="$(read_stage_marker)"
        if [[ -n "${report_json}" && -n "${marker_text}" ]]; then
            if verdict="$(validate_texture_stage \
                    "${report_json}" \
                    "${marker_text}" \
                    "${expected_facing}" \
                    "${capture_field}" \
                    "${previous_rotation}" 2>/dev/null)"; then
                printf '%s\n' "${report_json}" > "${report_destination}"
                printf '%s\n' "${marker_text}" > "${marker_destination}"
                printf '%s\n' "${verdict}" > "${verdict_destination}"
                return
            fi
            local status
            status="$(json_field "${report_json}" status)"
            if [[ "${status}" == "error" ]]; then
                printf 'Texture evidence faulted while waiting for %s: %s\n' \
                    "${description}" "${report_json}" >&2
                return 1
            fi
        fi
        if (( $(date +%s) >= deadline )); then
            printf 'Timed out waiting for %s; last report: %s; stage marker: %s\n' \
                "${description}" "${report_json:-none}" "${marker_text:-none}" >&2
            return 1
        fi
        if ! "${ADB[@]}" shell pidof "${PACKAGE_NAME}" >/dev/null; then
            printf 'Unity application exited while waiting for %s.\n' \
                "${description}" >&2
            return 1
        fi
        sleep "${POLL_SECONDS}"
    done
}

pull_optional_camera_texture()
{
    local verdict_path="$1"
    local remote_name="$2"
    local local_name="$3"
    if python3 - "${verdict_path}" <<'PY'
import json
import sys
from pathlib import Path
value = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
raise SystemExit(0 if value.get("remote_capture_written") is True else 1)
PY
    then
        pull_remote_evidence "${remote_name}" "${local_name}"
    fi
}

#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
PROFILE_PATH="${ROOT_DIR}/models/reachy-mini/upstream-baseline-stability.json"
HEADER_PATH="${ROOT_DIR}/native/reachy_sim/feasibility/reachy_stability_profile.generated.h"
OUTPUT_DIR="${MUJOCO_ANDROID_OUTPUT_DIR:-${ROOT_DIR}/Assets/Plugins/Android/libs/arm64-v8a}"
PROBE_BUILD_DIR="${REACHY_PROBE_ANDROID_BUILD_DIR:-${ROOT_DIR}/build/reachy-probe-android-arm64}"
RMA060_BUILD_INFO="${OUTPUT_DIR}/RMA060_BUILD_INFO.txt"

for required_file in \
    "${PROFILE_PATH}" \
    "${HEADER_PATH}" \
    "${SCRIPT_DIR}/build_mujoco_android.sh" \
    "${SCRIPT_DIR}/generate_reachy_stability_header.py"; do
    if [[ ! -f "${required_file}" ]]; then
        printf 'Required RMA-060 build input is missing: %s\n' "${required_file}" >&2
        exit 1
    fi
done

python3 "${SCRIPT_DIR}/generate_reachy_stability_header.py" \
    --profile "${PROFILE_PATH}" \
    --output "${HEADER_PATH}" \
    --check

"${SCRIPT_DIR}/build_mujoco_android.sh" "${1:-}"

cmake --build \
    "${PROBE_BUILD_DIR}" \
    --target reachy_mujoco_stability_runner \
    --parallel

RUNNER_PATH="$(find \
    "${PROBE_BUILD_DIR}" \
    -type f \
    -name 'reachy_mujoco_stability_runner' \
    -print \
    -quit)"
if [[ -z "${RUNNER_PATH}" ]]; then
    printf '%s\n' 'RMA-060 build omitted reachy_mujoco_stability_runner.' >&2
    exit 1
fi

mkdir -p "${OUTPUT_DIR}"
cp "${RUNNER_PATH}" "${OUTPUT_DIR}/reachy_mujoco_stability_runner"
cp "${PROFILE_PATH}" "${OUTPUT_DIR}/upstream-baseline-stability.json"

if [[ ! -f "${OUTPUT_DIR}/BUILD_INFO.txt" ]]; then
    printf 'Android build metadata is missing: %s\n' "${OUTPUT_DIR}/BUILD_INFO.txt" >&2
    exit 1
fi

python3 - "${PROFILE_PATH}" "${RMA060_BUILD_INFO}" <<'PY'
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

profile_path = Path(sys.argv[1])
output_path = Path(sys.argv[2])
raw = profile_path.read_bytes()
profile = json.loads(raw)
gate = profile["long_duration_gate"]
lines = [
    f"RMA-060 stability profile: {profile['profile_id']}",
    f"RMA-060 profile SHA-256: {hashlib.sha256(raw).hexdigest()}",
    f"RMA-060 physics timestep: {profile['timestep_seconds']} seconds (500 Hz)",
    f"RMA-060 required Android cycles: {gate['required_android_cycles']}",
    (
        "RMA-060 required simulated duration: "
        f"{gate['required_simulated_seconds']} seconds"
    ),
    (
        "RMA-060 representative hardware required: "
        f"{str(gate['representative_hardware_required']).lower()}"
    ),
    (
        "RMA-060 minimum solver realtime factor: "
        f"{gate['minimum_solver_realtime_factor']}"
    ),
    (
        "RMA-060 timestep deviation decision: "
        f"{gate['timestep_deviation_decision']}"
    ),
]
output_path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
PY

printf 'RMA-060 Android stability runner staged in %s\n' "${OUTPUT_DIR}"

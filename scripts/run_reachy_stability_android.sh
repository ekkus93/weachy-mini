#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
STAGING_DIR="${MUJOCO_ANDROID_OUTPUT_DIR:-${ROOT_DIR}/Assets/Plugins/Android/libs/arm64-v8a}"
REPORT_DIR="${REACHY_STABILITY_REPORT_DIR:-${ROOT_DIR}/diagnostics-output/rma060-stability}"
REMOTE_DIR="/data/local/tmp/weachy-rma060-stability"
REMOTE_MODEL_DIR="${REMOTE_DIR}/reachy-model"
ADB_BIN="${ADB:-adb}"
REQUESTED_SERIAL="${REACHY_ANDROID_SERIAL:-${ANDROID_SERIAL:-}}"
PROFILE_PATH="${STAGING_DIR}/upstream-baseline-stability.json"
MODEL_DIR="${STAGING_DIR}/reachy-model"
MODEL_PATH="${MODEL_DIR}/reachy_mini.xml"
DEVICE_SERIAL=""

failure_diagnostics()
{
    local status=$?
    if (( status == 0 )); then
        return
    fi

    trap - EXIT
    set +e
    printf 'RMA-060 Android stability gate failed with status %s.\n' "${status}" >&2
    if [[ -n "${DEVICE_SERIAL}" ]]; then
        "${ADB_BIN}" -s "${DEVICE_SERIAL}" shell \
            "find '${REMOTE_DIR}' -maxdepth 4 -type f -print 2>/dev/null | sort" >&2
    fi
    if [[ -d "${REPORT_DIR}" ]]; then
        find "${REPORT_DIR}" -maxdepth 1 -type f -print -exec sh -c \
            'printf "%s\n" "--- $1" >&2; cat "$1" >&2' sh {} \;
    fi
    exit "${status}"
}
trap failure_diagnostics EXIT

for required_file in \
    "${STAGING_DIR}/libmujoco.so" \
    "${STAGING_DIR}/reachy_mujoco_stability_runner" \
    "${STAGING_DIR}/RMA060_BUILD_INFO.txt" \
    "${PROFILE_PATH}" \
    "${MODEL_PATH}" \
    "${MODEL_DIR}/MODEL_MAP.json" \
    "${MODEL_DIR}/PROVENANCE.json"; do
    if [[ ! -f "${required_file}" ]]; then
        printf 'Missing staged RMA-060 input: %s\n' "${required_file}" >&2
        exit 1
    fi
done

command -v "${ADB_BIN}" >/dev/null

readarray -t GATE_VALUES < <(
    python3 - "${PROFILE_PATH}" <<'PY'
from __future__ import annotations

import json
import sys
from pathlib import Path

profile = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
gate = profile["long_duration_gate"]
print(gate["required_android_cycles"])
print(gate["required_simulated_seconds"])
print(gate["minimum_solver_realtime_factor"])
PY
)
REQUIRED_CYCLES="${GATE_VALUES[0]}"
REQUIRED_SIMULATED_SECONDS="${GATE_VALUES[1]}"
MINIMUM_REALTIME_FACTOR="${GATE_VALUES[2]}"
REQUESTED_CYCLES="${REACHY_STABILITY_CYCLES:-${REQUIRED_CYCLES}}"
if [[ "${REQUESTED_CYCLES}" != "${REQUIRED_CYCLES}" ]]; then
    printf 'RMA-060 acceptance requires exactly %s cycles; requested %s.\n' \
        "${REQUIRED_CYCLES}" "${REQUESTED_CYCLES}" >&2
    exit 1
fi

select_device_serial()
{
    if [[ -n "${REQUESTED_SERIAL}" ]]; then
        if ! "${ADB_BIN}" -s "${REQUESTED_SERIAL}" get-state 2>/dev/null \
            | grep -Fx 'device' >/dev/null; then
            printf 'Requested Android device is not online: %s\n' \
                "${REQUESTED_SERIAL}" >&2
            "${ADB_BIN}" devices -l >&2
            exit 1
        fi
        printf '%s\n' "${REQUESTED_SERIAL}"
        return
    fi

    local serial
    local abi
    local -a arm64_serials=()
    while IFS= read -r serial; do
        [[ -n "${serial}" ]] || continue
        abi="$("${ADB_BIN}" -s "${serial}" shell getprop ro.product.cpu.abi \
            | tr -d '\r')"
        if [[ "${abi}" == "arm64-v8a" ]]; then
            arm64_serials+=("${serial}")
        fi
    done < <(
        "${ADB_BIN}" devices \
            | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
    )

    if [[ "${#arm64_serials[@]}" -ne 1 ]]; then
        printf 'Exactly one physical arm64-v8a device is required; found %s.\n' \
            "${#arm64_serials[@]}" >&2
        "${ADB_BIN}" devices -l >&2
        exit 1
    fi
    printf '%s\n' "${arm64_serials[0]}"
}

validate_stability_report()
{
    local report_path="$1"
    python3 - \
        "${report_path}" \
        "${PROFILE_PATH}" \
        "${REQUESTED_CYCLES}" \
        "${REQUIRED_SIMULATED_SECONDS}" \
        "${MINIMUM_REALTIME_FACTOR}" <<'PY'
from __future__ import annotations

import hashlib
import json
import math
import sys
from pathlib import Path

report_path = Path(sys.argv[1])
profile_path = Path(sys.argv[2])
expected_cycles = int(sys.argv[3])
required_seconds = float(sys.argv[4])
minimum_realtime_factor = float(sys.argv[5])
profile_bytes = profile_path.read_bytes()
profile = json.loads(profile_bytes)
report = json.loads(report_path.read_text(encoding="utf-8"))

if report.get("status") != "ok":
    raise SystemExit(f"stability runner failed: {report}")
checks = {
    "profile_id": profile["profile_id"],
    "profile_sha256": hashlib.sha256(profile_bytes).hexdigest(),
    "source_model_sha256": profile["source"]["model_sha256"],
    "mujoco_version": profile["source"]["mujoco_version"],
    "upstream_commit": profile["source"]["upstream_commit"],
    "timestep_deviation_decision": profile["long_duration_gate"][
        "timestep_deviation_decision"
    ],
}
for key, expected in checks.items():
    if report.get(key) != expected:
        raise SystemExit(
            f"stability report {key} mismatch: expected {expected!r}, "
            f"found {report.get(key)!r}"
        )
if report.get("platform") != "android_arm64_physical":
    raise SystemExit(f"unexpected platform identity: {report.get('platform')!r}")
if report.get("cycles") != expected_cycles:
    raise SystemExit(f"unexpected cycle count: {report.get('cycles')!r}")
if report.get("phase_count_per_cycle") != len(profile["phases"]):
    raise SystemExit("phase count differs from the profile")
if report.get("timestep_seconds") != profile["timestep_seconds"]:
    raise SystemExit("stability timestep is not the exact 500 Hz profile timestep")

defaults = profile["phase_defaults"]
steps_per_phase = defaults["transition_steps"] + defaults["hold_steps"]
expected_steps = expected_cycles * len(profile["phases"]) * steps_per_phase
if report.get("completed_steps") != expected_steps:
    raise SystemExit(
        f"completed step mismatch: expected {expected_steps}, "
        f"found {report.get('completed_steps')}"
    )
simulated_seconds = report.get("simulated_seconds")
if not isinstance(simulated_seconds, (int, float)) or not math.isfinite(simulated_seconds):
    raise SystemExit("simulated duration is not finite")
if not math.isclose(simulated_seconds, required_seconds, rel_tol=0.0, abs_tol=1e-7):
    raise SystemExit(
        f"simulated duration mismatch: expected {required_seconds}, "
        f"found {simulated_seconds}"
    )
realtime_factor = report.get("solver_realtime_factor")
if not isinstance(realtime_factor, (int, float)) or not math.isfinite(realtime_factor):
    raise SystemExit("solver realtime factor is not finite")
if realtime_factor < minimum_realtime_factor:
    raise SystemExit(
        f"solver realtime factor {realtime_factor} is below {minimum_realtime_factor}"
    )

monitoring = profile["monitoring"]
metrics = report.get("aggregate_metrics")
if not isinstance(metrics, dict):
    raise SystemExit("aggregate_metrics is missing")
thresholds = {
    "maximum_equality_residual": monitoring["maximum_equality_residual"],
    "maximum_scalar_joint_limit_violation_radians": monitoring[
        "maximum_scalar_joint_limit_violation_radians"
    ],
    "maximum_contact_penetration_metres": monitoring[
        "maximum_contact_penetration_metres"
    ],
    "maximum_absolute_total_energy_joules": monitoring[
        "maximum_absolute_total_energy_joules"
    ],
}
for key, maximum in thresholds.items():
    actual = metrics.get(key)
    if not isinstance(actual, (int, float)) or not math.isfinite(actual):
        raise SystemExit(f"aggregate metric {key} is invalid: {actual!r}")
    if actual > maximum:
        raise SystemExit(f"aggregate metric {key} exceeds {maximum}: {actual}")
if metrics.get("completed_steps") != expected_steps:
    raise SystemExit("aggregate metric step count differs from the run")
if metrics.get("warning_count") != 0:
    raise SystemExit(f"MuJoCo warnings were reported: {metrics}")

phases = report.get("phases")
if not isinstance(phases, list) or len(phases) != len(profile["phases"]):
    raise SystemExit("phase report count differs from the profile")
actuator_indices = {name: index for index, name in enumerate(profile["actuator_names"])}
required_categories = {
    "neutral",
    "sleep",
    "body_yaw_limit",
    "head_actuator_limit",
    "antenna_extreme",
}
seen_categories: set[str] = set()
for expected_phase, actual_phase in zip(profile["phases"], phases, strict=True):
    if actual_phase.get("name") != expected_phase["name"]:
        raise SystemExit("phase names are not in canonical profile order")
    if actual_phase.get("category") != expected_phase["category"]:
        raise SystemExit("phase category differs from the profile")
    seen_categories.add(actual_phase["category"])
    expected_mask = 0
    for name in expected_phase["allowed_out_of_range_actuators"]:
        expected_mask |= 1 << actuator_indices[name]
    if actual_phase.get("allowed_out_of_range_mask") != expected_mask:
        raise SystemExit(f"range mask mismatch for {expected_phase['name']}")
    phase_metrics = actual_phase.get("metrics")
    if not isinstance(phase_metrics, dict):
        raise SystemExit(f"missing phase metrics for {expected_phase['name']}")
    expected_phase_steps = expected_cycles * steps_per_phase
    if phase_metrics.get("completed_steps") != expected_phase_steps:
        raise SystemExit(f"phase step count mismatch for {expected_phase['name']}")
    if phase_metrics.get("warning_count") != 0:
        raise SystemExit(f"warnings in phase {expected_phase['name']}")
if not required_categories.issubset(seen_categories):
    raise SystemExit(f"required stability categories are missing: {seen_categories}")

percentiles = report.get("timing_percentiles")
if not isinstance(percentiles, dict):
    raise SystemExit("timing_percentiles is missing")
for key in ("median_step_microseconds", "p95_step_microseconds"):
    value = percentiles.get(key)
    if not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0.0:
        raise SystemExit(f"timing percentile {key} is invalid: {value!r}")

print(json.dumps(report, indent=2, sort_keys=True))
PY
}

DEVICE_SERIAL="$(select_device_serial)"
ADB_COMMAND=("${ADB_BIN}" -s "${DEVICE_SERIAL}")
DEVICE_ABI="$("${ADB_COMMAND[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
if [[ "${DEVICE_ABI}" != "arm64-v8a" ]]; then
    printf 'Selected device ABI is %s, expected arm64-v8a.\n' "${DEVICE_ABI}" >&2
    exit 1
fi

mkdir -p "${REPORT_DIR}"
TIMESTAMP="$(date -u +'%Y%m%dT%H%M%SZ')"
REPORT_PATH="${REPORT_DIR}/${TIMESTAMP}-rma060-android-stability.json"
DEVICE_PATH="${REPORT_DIR}/${TIMESTAMP}-device.txt"
THERMAL_BEFORE_PATH="${REPORT_DIR}/${TIMESTAMP}-thermal-before.txt"
THERMAL_AFTER_PATH="${REPORT_DIR}/${TIMESTAMP}-thermal-after.txt"
FAILURE_PATH="${REPORT_DIR}/${TIMESTAMP}-invalid-cycles.json"

{
    printf 'serial=%s\n' "${DEVICE_SERIAL}"
    printf 'manufacturer=%s\n' \
        "$("${ADB_COMMAND[@]}" shell getprop ro.product.manufacturer | tr -d '\r')"
    printf 'model=%s\n' \
        "$("${ADB_COMMAND[@]}" shell getprop ro.product.model | tr -d '\r')"
    printf 'device=%s\n' \
        "$("${ADB_COMMAND[@]}" shell getprop ro.product.device | tr -d '\r')"
    printf 'android_release=%s\n' \
        "$("${ADB_COMMAND[@]}" shell getprop ro.build.version.release | tr -d '\r')"
    printf 'sdk=%s\n' \
        "$("${ADB_COMMAND[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
    printf 'abi=%s\n' "${DEVICE_ABI}"
    printf 'required_cycles=%s\n' "${REQUIRED_CYCLES}"
    printf 'required_simulated_seconds=%s\n' "${REQUIRED_SIMULATED_SECONDS}"
    printf 'minimum_solver_realtime_factor=%s\n' "${MINIMUM_REALTIME_FACTOR}"
    printf 'profile_sha256=%s\n' "$(sha256sum "${PROFILE_PATH}" | awk '{print $1}')"
} > "${DEVICE_PATH}"

"${ADB_COMMAND[@]}" shell dumpsys thermalservice \
    | tr -d '\r' > "${THERMAL_BEFORE_PATH}" || true

"${ADB_COMMAND[@]}" shell \
    "rm -rf '${REMOTE_DIR}' && mkdir -p '${REMOTE_MODEL_DIR}'"
for runtime_file in libmujoco.so reachy_mujoco_stability_runner; do
    "${ADB_COMMAND[@]}" push \
        "${STAGING_DIR}/${runtime_file}" \
        "${REMOTE_DIR}/${runtime_file}" >/dev/null
done
"${ADB_COMMAND[@]}" push "${PROFILE_PATH}" "${REMOTE_DIR}/" >/dev/null
"${ADB_COMMAND[@]}" push "${MODEL_DIR}/." "${REMOTE_MODEL_DIR}/" >/dev/null
"${ADB_COMMAND[@]}" shell \
    "chmod 700 '${REMOTE_DIR}/reachy_mujoco_stability_runner'"

set +e
INVALID_COMMAND="cd '${REMOTE_DIR}' && LD_LIBRARY_PATH='${REMOTE_DIR}' "
INVALID_COMMAND+="./reachy_mujoco_stability_runner "
INVALID_COMMAND+="reachy-model/reachy_mini.xml 0 android_arm64_physical"
"${ADB_COMMAND[@]}" shell "${INVALID_COMMAND}" \
    | tr -d '\r' > "${FAILURE_PATH}"
INVALID_STATUS=${PIPESTATUS[0]}
set -e
if (( INVALID_STATUS == 0 )); then
    printf '%s\n' 'Invalid-cycle failure path unexpectedly succeeded.' >&2
    exit 1
fi
python3 - "${FAILURE_PATH}" <<'PY'
from __future__ import annotations

import json
import sys
from pathlib import Path

report = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
if report.get("status") != "failed":
    raise SystemExit(f"invalid cycles did not produce structured failure: {report}")
if "positive 32-bit integer" not in report.get("error", ""):
    raise SystemExit(f"invalid cycles produced the wrong failure: {report}")
PY

STABILITY_COMMAND="cd '${REMOTE_DIR}' && LD_LIBRARY_PATH='${REMOTE_DIR}' "
STABILITY_COMMAND+="./reachy_mujoco_stability_runner "
STABILITY_COMMAND+="reachy-model/reachy_mini.xml "
STABILITY_COMMAND+="'${REQUESTED_CYCLES}' android_arm64_physical"
"${ADB_COMMAND[@]}" shell "${STABILITY_COMMAND}" \
    | tr -d '\r' > "${REPORT_PATH}"
validate_stability_report "${REPORT_PATH}"

"${ADB_COMMAND[@]}" shell dumpsys thermalservice \
    | tr -d '\r' > "${THERMAL_AFTER_PATH}" || true

trap - EXIT
printf '%s\n' \
    "RMA-060 stability report: ${REPORT_PATH}" \
    "RMA-060 device report: ${DEVICE_PATH}" \
    "RMA-060 failure-path report: ${FAILURE_PATH}" \
    "RMA-060 thermal before: ${THERMAL_BEFORE_PATH}" \
    "RMA-060 thermal after: ${THERMAL_AFTER_PATH}"

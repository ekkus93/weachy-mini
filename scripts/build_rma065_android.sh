#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
OUTPUT_DIR="${MUJOCO_ANDROID_OUTPUT_DIR:-${ROOT_DIR}/build/rma065-android-artifact}"
PROBE_BUILD_DIR="${REACHY_PROBE_ANDROID_BUILD_DIR:-${ROOT_DIR}/build/rma065-probe-android-arm64}"
SOURCE_MODEL_DIR="${RMA065_SOURCE_MODEL_DIR:?RMA065_SOURCE_MODEL_DIR is required}"
ENHANCED_MODEL_DIR="${RMA065_ENHANCED_MODEL_DIR:?RMA065_ENHANCED_MODEL_DIR is required}"
PROFILE_PATH="${ROOT_DIR}/models/reachy-mini/collision-hard-stop-baseline.json"

for required in \
    "${SOURCE_MODEL_DIR}/reachy_mini.xml" \
    "${ENHANCED_MODEL_DIR}/reachy_mini.xml" \
    "${PROFILE_PATH}" \
    "${SCRIPT_DIR}/build_mujoco_android.sh"; do
    if [[ ! -f "${required}" ]]; then
        printf 'Required RMA-065 Android input is missing: %s\n' "${required}" >&2
        exit 1
    fi
done

MUJOCO_ANDROID_OUTPUT_DIR="${OUTPUT_DIR}" \
REACHY_PROBE_ANDROID_BUILD_DIR="${PROBE_BUILD_DIR}" \
    bash "${SCRIPT_DIR}/build_mujoco_android.sh"

cmake --build \
    "${PROBE_BUILD_DIR}" \
    --target reachy_mujoco_collision_benchmark_runner \
    --parallel

RUNNER_PATH="$(find \
    "${PROBE_BUILD_DIR}" \
    -type f \
    -name reachy_mujoco_collision_benchmark_runner \
    -print \
    -quit)"
if [[ -z "${RUNNER_PATH}" ]]; then
    printf '%s\n' 'RMA-065 Android build omitted the collision benchmark runner.' >&2
    exit 1
fi

rm -rf "${OUTPUT_DIR}/source-model" "${OUTPUT_DIR}/enhanced-model"
mkdir -p "${OUTPUT_DIR}"
cp "${RUNNER_PATH}" "${OUTPUT_DIR}/reachy_mujoco_collision_benchmark_runner"
cp -a "${SOURCE_MODEL_DIR}" "${OUTPUT_DIR}/source-model"
cp -a "${ENHANCED_MODEL_DIR}" "${OUTPUT_DIR}/enhanced-model"
cp "${PROFILE_PATH}" "${OUTPUT_DIR}/collision-hard-stop-baseline.json"

for required in \
    "${OUTPUT_DIR}/libmujoco.so" \
    "${OUTPUT_DIR}/reachy_mujoco_collision_benchmark_runner" \
    "${OUTPUT_DIR}/source-model/reachy_mini.xml" \
    "${OUTPUT_DIR}/enhanced-model/reachy_mini.xml" \
    "${OUTPUT_DIR}/collision-hard-stop-baseline.json"; do
    if [[ ! -s "${required}" ]]; then
        printf 'RMA-065 Android artifact is incomplete: %s\n' "${required}" >&2
        exit 1
    fi
done

printf 'RMA-065 Android benchmark artifact staged in %s\n' "${OUTPUT_DIR}"

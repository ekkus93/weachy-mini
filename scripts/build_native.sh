#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
BUILD_DIR="${REACHY_NATIVE_BUILD_DIR:-${ROOT_DIR}/build/native}"
BUILD_TYPE="${REACHY_BUILD_TYPE:-Debug}"
SANITIZERS="${REACHY_ENABLE_SANITIZERS:-OFF}"

cmake \
    -S "${ROOT_DIR}" \
    -B "${BUILD_DIR}" \
    -DCMAKE_BUILD_TYPE="${BUILD_TYPE}" \
    -DREACHY_ENABLE_SANITIZERS="${SANITIZERS}" \
    -DCMAKE_EXPORT_COMPILE_COMMANDS=ON
cmake --build "${BUILD_DIR}" --parallel
ctest --test-dir "${BUILD_DIR}" --output-on-failure

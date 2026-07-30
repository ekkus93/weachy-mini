#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
SOURCE_DIR="${MUJOCO_SOURCE_DIR:-${1:-}}"
LOCK_FILE="${ROOT_DIR}/third_party/mujoco-source.lock.json"
TOOLCHAIN_FILE="${ROOT_DIR}/toolchain.lock.json"
REFERENCE_SCENARIO="${ROOT_DIR}/models/reachy-mini/reference-scenario.json"
BUILD_DIR="${MUJOCO_ANDROID_BUILD_DIR:-${ROOT_DIR}/build/mujoco-android-arm64}"
OUTPUT_DIR="${MUJOCO_ANDROID_OUTPUT_DIR:-${ROOT_DIR}/Assets/Plugins/Android/libs/arm64-v8a}"
COMPAT_DIR="${ROOT_DIR}/native/reachy_sim/compat/android_api26"
COMPAT_CMAKE="${COMPAT_DIR}/reachy_mujoco_android_api26.cmake"
COMPAT_SOURCE="${COMPAT_DIR}/reachy_android_api26_compat.c"
COMPAT_HEADER="${COMPAT_DIR}/reachy_android_api26_compat.h"
DEFAULT_ANDROID_PLATFORM="$(python3 - "${TOOLCHAIN_FILE}" <<'PY'
import json
import sys
from pathlib import Path

manifest = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
print(f"android-{manifest['android']['native_feasibility_min_sdk']}")
PY
)"
ANDROID_PLATFORM="${MUJOCO_ANDROID_PLATFORM:-${DEFAULT_ANDROID_PLATFORM}}"
EXPECTED_NDK="28.2.13676358"

if [[ -z "${SOURCE_DIR}" ]]; then
    printf '%s\n' "usage: MUJOCO_SOURCE_DIR=/path/to/mujoco $0" >&2
    exit 2
fi

for required_file in \
    "${COMPAT_CMAKE}" \
    "${COMPAT_SOURCE}" \
    "${COMPAT_HEADER}" \
    "${REFERENCE_SCENARIO}"; do
    if [[ ! -f "${required_file}" ]]; then
        printf 'Required Android build input is missing: %s\n' \
            "${required_file}" >&2
        exit 1
    fi
done

NDK_ROOT="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}"
if [[ -z "${NDK_ROOT}" ]]; then
    printf '%s\n' "ANDROID_NDK_HOME or ANDROID_NDK_ROOT must point to NDK ${EXPECTED_NDK}." >&2
    exit 1
fi

SOURCE_PROPERTIES="${NDK_ROOT}/source.properties"
NDK_TOOLCHAIN_FILE="${NDK_ROOT}/build/cmake/android.toolchain.cmake"
if [[ ! -f "${SOURCE_PROPERTIES}" || ! -f "${NDK_TOOLCHAIN_FILE}" ]]; then
    printf 'Android NDK is incomplete at %s.\n' "${NDK_ROOT}" >&2
    exit 1
fi

ACTUAL_NDK="$(sed -n 's/^Pkg.Revision[[:space:]]*=[[:space:]]*//p' "${SOURCE_PROPERTIES}")"
if [[ "${ACTUAL_NDK}" != "${EXPECTED_NDK}" ]]; then
    printf 'Android NDK mismatch: expected %s, found %s.\n' "${EXPECTED_NDK}" "${ACTUAL_NDK}" >&2
    exit 1
fi

python3 "${SCRIPT_DIR}/verify_source_checkout.py" \
    --source "${SOURCE_DIR}" \
    --lock "${LOCK_FILE}"
python3 "${SCRIPT_DIR}/generate_reachy_reference_header.py" \
    --scenario "${REFERENCE_SCENARIO}" \
    --output "${ROOT_DIR}/native/reachy_sim/feasibility/reachy_reference_scenario.generated.h" \
    --check

command -v cmake >/dev/null
command -v ninja >/dev/null
command -v sha256sum >/dev/null

cmake \
    -S "${SOURCE_DIR}" \
    -B "${BUILD_DIR}" \
    -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="${NDK_TOOLCHAIN_FILE}" \
    -DCMAKE_PROJECT_INCLUDE="${COMPAT_CMAKE}" \
    -DREACHY_MUJOCO_ANDROID_COMPAT_SOURCE="${COMPAT_SOURCE}" \
    -DREACHY_MUJOCO_ANDROID_COMPAT_HEADER="${COMPAT_HEADER}" \
    -DANDROID_ABI=arm64-v8a \
    -DANDROID_PLATFORM="${ANDROID_PLATFORM}" \
    -DANDROID_STL=c++_static \
    -DCMAKE_BUILD_TYPE=Release \
    -DMUJOCO_BUILD_EXAMPLES=OFF \
    -DMUJOCO_BUILD_SIMULATE=OFF \
    -DMUJOCO_BUILD_STUDIO=OFF \
    -DMUJOCO_BUILD_TESTS=OFF \
    -DMUJOCO_TEST_PYTHON_UTIL=OFF \
    -DMUJOCO_WITH_USD=OFF \
    -DMUJOCO_USE_FILAMENT=OFF

cmake --build "${BUILD_DIR}" --target mujoco --parallel

LIBRARY_PATH="$(find "${BUILD_DIR}" -type f -name 'libmujoco.so' -print -quit)"
if [[ -z "${LIBRARY_PATH}" ]]; then
    printf '%s\n' "MuJoCo build completed without producing libmujoco.so." >&2
    exit 1
fi

mkdir -p "${OUTPUT_DIR}"
cp "${LIBRARY_PATH}" "${OUTPUT_DIR}/libmujoco.so"

PROBE_BUILD_DIR="${REACHY_PROBE_ANDROID_BUILD_DIR:-${ROOT_DIR}/build/reachy-probe-android-arm64}"
cmake \
    -S "${ROOT_DIR}" \
    -B "${PROBE_BUILD_DIR}" \
    -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="${NDK_TOOLCHAIN_FILE}" \
    -DANDROID_ABI=arm64-v8a \
    -DANDROID_PLATFORM="${ANDROID_PLATFORM}" \
    -DANDROID_STL=c++_static \
    -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_TESTING=OFF \
    -DREACHY_BUILD_MUJOCO_PROBE=ON \
    -DREACHY_BUILD_MUJOCO_BACKEND=ON \
    -DREACHY_MUJOCO_EXPECTED_VERSION=3.9.0 \
    -DREACHY_MUJOCO_INCLUDE_DIR="${SOURCE_DIR}/include" \
    -DREACHY_MUJOCO_LIBRARY="${OUTPUT_DIR}/libmujoco.so"
cmake --build \
    "${PROBE_BUILD_DIR}" \
    --target \
        reachy_mujoco_probe_runner \
        reachy_mujoco_reference_runner \
        reachy_mujoco_compile_runner \
        reachy_sim_shared \
        reachy_sim_backend_runner \
    --parallel

PROBE_PATH="$(find "${PROBE_BUILD_DIR}" -type f -name 'reachy_mujoco_probe_runner' -print -quit)"
REFERENCE_RUNNER_PATH="$(find "${PROBE_BUILD_DIR}" -type f -name 'reachy_mujoco_reference_runner' -print -quit)"
COMPILE_RUNNER_PATH="$(find "${PROBE_BUILD_DIR}" -type f -name 'reachy_mujoco_compile_runner' -print -quit)"
BACKEND_RUNNER_PATH="$(find "${PROBE_BUILD_DIR}" -type f -name 'reachy_sim_backend_runner' -print -quit)"
REACHY_SIM_LIBRARY_PATH="$(find "${PROBE_BUILD_DIR}" -type f -name 'libreachy_sim.so' -print -quit)"
for produced_file in \
    "${PROBE_PATH}" \
    "${REFERENCE_RUNNER_PATH}" \
    "${COMPILE_RUNNER_PATH}" \
    "${BACKEND_RUNNER_PATH}" \
    "${REACHY_SIM_LIBRARY_PATH}"; do
    if [[ -z "${produced_file}" ]]; then
        printf '%s\n' "Production backend/probe build omitted a required artifact." >&2
        exit 1
    fi
done
cp "${PROBE_PATH}" "${OUTPUT_DIR}/reachy_mujoco_probe_runner"
cp "${REFERENCE_RUNNER_PATH}" "${OUTPUT_DIR}/reachy_mujoco_reference_runner"
cp "${COMPILE_RUNNER_PATH}" "${OUTPUT_DIR}/reachy_mujoco_compile_runner"
cp "${BACKEND_RUNNER_PATH}" "${OUTPUT_DIR}/reachy_sim_backend_runner"
cp "${REACHY_SIM_LIBRARY_PATH}" "${OUTPUT_DIR}/libreachy_sim.so"
cp "${REFERENCE_SCENARIO}" "${OUTPUT_DIR}/reference-scenario.json"
cp \
    "${ROOT_DIR}/native/reachy_sim/tests/fixtures/closed_loop_probe.xml" \
    "${OUTPUT_DIR}/closed_loop_probe.xml"
cp \
    "${ROOT_DIR}/native/reachy_sim/tests/fixtures/malformed_probe.xml" \
    "${OUTPUT_DIR}/malformed_probe.xml"

HOST_TAG="$(python3 - "${NDK_ROOT}" <<'PY'
from pathlib import Path
import sys

prebuilt = Path(sys.argv[1]) / "toolchains/llvm/prebuilt"
candidates = sorted(path for path in prebuilt.iterdir() if path.is_dir())
if len(candidates) != 1:
    raise SystemExit(f"expected one NDK host toolchain, found {len(candidates)}")
print(candidates[0])
PY
)"
READELF="${HOST_TAG}/bin/llvm-readelf"
NM="${HOST_TAG}/bin/llvm-nm"
if [[ ! -x "${READELF}" || ! -x "${NM}" ]]; then
    printf '%s\n' "NDK LLVM inspection tools are missing." >&2
    exit 1
fi

"${READELF}" -d "${OUTPUT_DIR}/libmujoco.so" > "${OUTPUT_DIR}/libmujoco.dynamic.txt"
"${NM}" -D --defined-only "${OUTPUT_DIR}/libmujoco.so" > "${OUTPUT_DIR}/libmujoco.exports.txt"
"${NM}" -D --undefined-only "${OUTPUT_DIR}/libmujoco.so" > "${OUTPUT_DIR}/libmujoco.imports.txt"
"${NM}" --defined-only "${OUTPUT_DIR}/libmujoco.so" > "${OUTPUT_DIR}/libmujoco.symbols.txt"
"${READELF}" -d "${OUTPUT_DIR}/libreachy_sim.so" > "${OUTPUT_DIR}/libreachy_sim.dynamic.txt"
"${NM}" -D --defined-only "${OUTPUT_DIR}/libreachy_sim.so" > "${OUTPUT_DIR}/libreachy_sim.exports.txt"

if grep -E 'lib(GL|GLX|X11|glfw)' "${OUTPUT_DIR}/libmujoco.dynamic.txt"; then
    printf '%s\n' "Desktop-only dependency detected in Android MuJoCo library." >&2
    exit 1
fi
if grep -E 'lib(GL|GLX|X11|glfw)' "${OUTPUT_DIR}/libreachy_sim.dynamic.txt"; then
    printf '%s\n' "Desktop-only dependency detected in Android reachy_sim library." >&2
    exit 1
fi
if ! grep -F 'Shared library: [libmujoco.so]' \
    "${OUTPUT_DIR}/libreachy_sim.dynamic.txt" >/dev/null; then
    printf '%s\n' "Production reachy_sim library is not linked to libmujoco.so." >&2
    exit 1
fi
if ! grep -E '[[:space:]]reachy_sim_create$' \
    "${OUTPUT_DIR}/libreachy_sim.exports.txt" >/dev/null; then
    printf '%s\n' "Production reachy_sim ABI exports are missing." >&2
    exit 1
fi
if ! grep -E '[[:space:]]reachy_android_api26_aligned_alloc$' \
    "${OUTPUT_DIR}/libmujoco.symbols.txt" >/dev/null; then
    printf '%s\n' "Android API 26 allocation compatibility symbol is missing." >&2
    exit 1
fi
if grep -E '[[:space:]]aligned_alloc$' \
    "${OUTPUT_DIR}/libmujoco.imports.txt" >/dev/null; then
    printf '%s\n' "Android MuJoCo library still imports unavailable aligned_alloc." >&2
    exit 1
fi

REFERENCE_SCENARIO_SHA256="$(sha256sum "${REFERENCE_SCENARIO}" | awk '{print $1}')"
cat > "${OUTPUT_DIR}/BUILD_INFO.txt" <<INFO
MuJoCo version: 3.9.0
MuJoCo commit: 237c17e48539b6c90bf90d3161547cbdcbfaa1e0
reachy_sim backend: production MuJoCo
Android ABI: arm64-v8a
Android platform: ${ANDROID_PLATFORM}
Android NDK: ${ACTUAL_NDK}
Build type: Release
Android STL: c++_static
Android API 26 allocation compatibility: hidden posix_memalign shim
POSIX time feature level: 200809L
Reference scenario: rma042_representative_motion_v1
Reference scenario SHA-256: ${REFERENCE_SCENARIO_SHA256}
Third-party source modified: no
INFO

printf 'MuJoCo Android library, production backend, and probes staged in %s\n' "${OUTPUT_DIR}"

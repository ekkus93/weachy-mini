#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
SOURCE_DIR="${MUJOCO_SOURCE_DIR:-${1:-}}"
LOCK_FILE="${ROOT_DIR}/third_party/mujoco-source.lock.json"
TOOLCHAIN_FILE="${ROOT_DIR}/toolchain.lock.json"
BUILD_DIR="${MUJOCO_ANDROID_BUILD_DIR:-${ROOT_DIR}/build/mujoco-android-arm64}"
OUTPUT_DIR="${MUJOCO_ANDROID_OUTPUT_DIR:-${ROOT_DIR}/Assets/Plugins/Android/libs/arm64-v8a}"
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

command -v cmake >/dev/null
command -v ninja >/dev/null

cmake \
    -S "${SOURCE_DIR}" \
    -B "${BUILD_DIR}" \
    -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="${NDK_TOOLCHAIN_FILE}" \
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
    -DREACHY_MUJOCO_INCLUDE_DIR="${SOURCE_DIR}/include" \
    -DREACHY_MUJOCO_LIBRARY="${OUTPUT_DIR}/libmujoco.so"
cmake --build "${PROBE_BUILD_DIR}" --target reachy_mujoco_probe_runner --parallel

PROBE_PATH="$(find "${PROBE_BUILD_DIR}" -type f -name 'reachy_mujoco_probe_runner' -print -quit)"
if [[ -z "${PROBE_PATH}" ]]; then
    printf '%s\n' "Probe build completed without producing reachy_mujoco_probe_runner." >&2
    exit 1
fi
cp "${PROBE_PATH}" "${OUTPUT_DIR}/reachy_mujoco_probe_runner"
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

if grep -E 'lib(GL|GLX|X11|glfw)' "${OUTPUT_DIR}/libmujoco.dynamic.txt"; then
    printf '%s\n' "Desktop-only dependency detected in Android MuJoCo library." >&2
    exit 1
fi

cat > "${OUTPUT_DIR}/BUILD_INFO.txt" <<INFO
MuJoCo version: 3.9.0
MuJoCo commit: 237c17e48539b6c90bf90d3161547cbdcbfaa1e0
Android ABI: arm64-v8a
Android platform: ${ANDROID_PLATFORM}
Android NDK: ${ACTUAL_NDK}
Build type: Release
Android STL: c++_static
Third-party source modified: no
INFO

printf 'MuJoCo Android library and probe staged in %s\n' "${OUTPUT_DIR}"

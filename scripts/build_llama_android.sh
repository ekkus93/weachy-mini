#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
SOURCE_DIR="${LLAMA_CPP_SOURCE_DIR:-${1:-}}"
LOCK_FILE="${ROOT_DIR}/third_party/llama-cpp-source.lock.json"
TOOLCHAIN_FILE="${ROOT_DIR}/toolchain.lock.json"
BUILD_DIR="${LLAMA_ANDROID_BUILD_DIR:-${ROOT_DIR}/build/llama-android-arm64}"
OUTPUT_DIR="${LLAMA_ANDROID_OUTPUT_DIR:-${ROOT_DIR}/build/llama-android-output}"

if [[ -z "${SOURCE_DIR}" ]]; then
    printf '%s\n' "usage: LLAMA_CPP_SOURCE_DIR=/path/to/llama.cpp $0" >&2
    exit 2
fi

mapfile -t pins < <(
    python3 - "${TOOLCHAIN_FILE}" "${LOCK_FILE}" <<'PY'
import json
import sys
from pathlib import Path

toolchain = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
lock = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
android = toolchain["android"]
for value in (
    android["ndk"],
    android["native_feasibility_min_sdk"],
    android["abi"],
    lock["version"],
    lock["commit"],
    lock["license_git_blob"],
):
    print(value)
PY
)
if (( ${#pins[@]} != 6 )); then
    printf '%s\n' 'Could not read RMA-130 source/toolchain pins.' >&2
    exit 1
fi

EXPECTED_NDK="${pins[0]}"
DEFAULT_MIN_SDK="${pins[1]}"
EXPECTED_ABI="${pins[2]}"
LLAMA_VERSION="${pins[3]}"
LLAMA_COMMIT="${pins[4]}"
EXPECTED_LICENSE_BLOB="${pins[5]}"
ANDROID_PLATFORM="${LLAMA_ANDROID_PLATFORM:-android-${DEFAULT_MIN_SDK}}"
ANDROID_ABI="${LLAMA_ANDROID_ABI:-${EXPECTED_ABI}}"

if [[ "${ANDROID_ABI}" != "arm64-v8a" ]]; then
    printf 'RMA-130 supports only arm64-v8a; requested %s.\n' "${ANDROID_ABI}" >&2
    exit 1
fi

NDK_ROOT="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}"
if [[ -z "${NDK_ROOT}" ]]; then
    printf 'ANDROID_NDK_HOME or ANDROID_NDK_ROOT must point to NDK %s.\n' \
        "${EXPECTED_NDK}" >&2
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
    printf 'Android NDK mismatch: expected %s, found %s.\n' \
        "${EXPECTED_NDK}" "${ACTUAL_NDK}" >&2
    exit 1
fi

python3 "${SCRIPT_DIR}/verify_source_checkout.py" \
    --source "${SOURCE_DIR}" \
    --lock "${LOCK_FILE}"
ACTUAL_LICENSE_BLOB="$(git -C "${SOURCE_DIR}" hash-object "${SOURCE_DIR}/LICENSE")"
if [[ "${ACTUAL_LICENSE_BLOB}" != "${EXPECTED_LICENSE_BLOB}" ]]; then
    printf 'llama.cpp license blob mismatch: expected %s, found %s.\n' \
        "${EXPECTED_LICENSE_BLOB}" "${ACTUAL_LICENSE_BLOB}" >&2
    exit 1
fi

command -v cmake >/dev/null
command -v ninja >/dev/null
command -v sha256sum >/dev/null

rm -rf -- "${BUILD_DIR}" "${OUTPUT_DIR}"
cmake \
    -S "${ROOT_DIR}" \
    -B "${BUILD_DIR}" \
    -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="${NDK_TOOLCHAIN_FILE}" \
    -DANDROID_ABI="${ANDROID_ABI}" \
    -DANDROID_PLATFORM="${ANDROID_PLATFORM}" \
    -DANDROID_STL=c++_static \
    -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_TESTING=OFF \
    -DREACHY_BUILD_LLAMA_RUNTIME=ON \
    -DREACHY_LLAMA_CPP_SOURCE_DIR="${SOURCE_DIR}"
cmake --build "${BUILD_DIR}" --target reachy_llama --parallel

LIBRARY_PATH="$(find "${BUILD_DIR}" -type f -name 'libreachy_llama.so' -print -quit)"
if [[ -z "${LIBRARY_PATH}" || ! -s "${LIBRARY_PATH}" ]]; then
    printf '%s\n' 'RMA-130 Android build did not produce libreachy_llama.so.' >&2
    exit 1
fi
mkdir -p "${OUTPUT_DIR}"
cp "${LIBRARY_PATH}" "${OUTPUT_DIR}/libreachy_llama.so"
cp "${SOURCE_DIR}/LICENSE" "${OUTPUT_DIR}/LLAMA_CPP_LICENSE.txt"

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
    printf '%s\n' 'NDK LLVM inspection tools are missing.' >&2
    exit 1
fi

"${READELF}" -d "${OUTPUT_DIR}/libreachy_llama.so" > \
    "${OUTPUT_DIR}/libreachy_llama.dynamic.txt"
"${NM}" -D --defined-only "${OUTPUT_DIR}/libreachy_llama.so" > \
    "${OUTPUT_DIR}/libreachy_llama.exports.txt"
"${NM}" -D --undefined-only "${OUTPUT_DIR}/libreachy_llama.so" > \
    "${OUTPUT_DIR}/libreachy_llama.imports.txt"

if grep -E 'lib(llama|ggml|c\+\+_shared|GL|GLX|X11|glfw)' \
    "${OUTPUT_DIR}/libreachy_llama.dynamic.txt"; then
    printf '%s\n' 'RMA-130 Android library has a prohibited dynamic dependency.' >&2
    exit 1
fi

UNEXPECTED_EXPORTS="$(
    awk '{print $NF}' "${OUTPUT_DIR}/libreachy_llama.exports.txt" \
        | grep -Ev '^(reachy_llama_|REACHY_LLAMA_1$|$)' || true
)"
if [[ -n "${UNEXPECTED_EXPORTS}" ]]; then
    printf '%s\n' 'RMA-130 leaked symbols outside the first-party ABI:' >&2
    printf '%s\n' "${UNEXPECTED_EXPORTS}" >&2
    exit 1
fi
for required_symbol in \
    reachy_llama_abi_version \
    reachy_llama_model_load \
    reachy_llama_tokenize \
    reachy_llama_apply_chat_template \
    reachy_llama_generation_start \
    reachy_llama_generation_poll \
    reachy_llama_generation_cancel \
    reachy_llama_generation_release \
    reachy_llama_generation_get_metrics \
    reachy_llama_model_unload; do
    if ! grep -E "[[:space:]]${required_symbol}(@@REACHY_LLAMA_1)?$" \
        "${OUTPUT_DIR}/libreachy_llama.exports.txt" >/dev/null; then
        printf 'Required RMA-130 export is missing: %s\n' "${required_symbol}" >&2
        exit 1
    fi
done

LIBRARY_SHA256="$(sha256sum "${OUTPUT_DIR}/libreachy_llama.so" | awk '{print $1}')"
cat > "${OUTPUT_DIR}/LLAMA_BUILD_INFO.txt" <<INFO
RMA: RMA-130
llama.cpp release: ${LLAMA_VERSION}
llama.cpp commit: ${LLAMA_COMMIT}
Android ABI: ${ANDROID_ABI}
Android platform: ${ANDROID_PLATFORM}
Android NDK: ${ACTUAL_NDK}
Android STL: c++_static
CPU baseline: armv8-a
CPU backend: static CPU-only baseline
GGML_NATIVE: OFF
GGML_OPENMP: OFF
GGML_LLAMAFILE: OFF
Dynamic upstream backends: OFF
Model bundled: no
libreachy_llama.so SHA-256: ${LIBRARY_SHA256}
INFO

printf 'RMA-130 Android runtime built and inspected: %s\n' \
    "${OUTPUT_DIR}/libreachy_llama.so"

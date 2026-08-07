#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
TOOLCHAIN_FILE="${ROOT_DIR}/toolchain.lock.json"
RUNTIME_DIR="${RMA133_RUNTIME_DIR:-${ROOT_DIR}/build/rma133/runtime}"
OUTPUT_DIR="${RMA133_BENCHMARK_OUTPUT_DIR:-${ROOT_DIR}/build/rma133/benchmark}"
SOURCE_FILE="${ROOT_DIR}/native/llama_runtime/benchmark/rma133_benchmark.c"
HEADER_DIR="${ROOT_DIR}/native/llama_runtime/include"

mapfile -t pins < <(
    python3 - "${TOOLCHAIN_FILE}" <<'PY'
import json
import sys
from pathlib import Path

android = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))["android"]
print(android["ndk"])
print(android["native_feasibility_min_sdk"])
print(android["abi"])
PY
)
if (( ${#pins[@]} != 3 )); then
    printf '%s\n' 'Could not read the RMA-133 Android toolchain pins.' >&2
    exit 1
fi
EXPECTED_NDK="${pins[0]}"
MIN_SDK="${pins[1]}"
EXPECTED_ABI="${pins[2]}"
if [[ "${EXPECTED_ABI}" != "arm64-v8a" ]]; then
    printf 'Unexpected Android ABI in toolchain lock: %s\n' "${EXPECTED_ABI}" >&2
    exit 1
fi

NDK_ROOT="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}"
if [[ -z "${NDK_ROOT}" ]]; then
    printf 'ANDROID_NDK_HOME or ANDROID_NDK_ROOT must point to NDK %s.\n' "${EXPECTED_NDK}" >&2
    exit 1
fi
SOURCE_PROPERTIES="${NDK_ROOT}/source.properties"
if [[ ! -f "${SOURCE_PROPERTIES}" ]]; then
    printf 'Android NDK is incomplete at %s.\n' "${NDK_ROOT}" >&2
    exit 1
fi
ACTUAL_NDK="$(sed -n 's/^Pkg.Revision[[:space:]]*=[[:space:]]*//p' "${SOURCE_PROPERTIES}")"
if [[ "${ACTUAL_NDK}" != "${EXPECTED_NDK}" ]]; then
    printf 'Android NDK mismatch: expected %s, found %s.\n' "${EXPECTED_NDK}" "${ACTUAL_NDK}" >&2
    exit 1
fi

RUNTIME_LIBRARY="${RUNTIME_DIR}/libreachy_llama.so"
if [[ ! -s "${RUNTIME_LIBRARY}" ]]; then
    printf 'RMA-133 requires the RMA-130 Android runtime at %s.\n' "${RUNTIME_LIBRARY}" >&2
    exit 1
fi
if [[ ! -f "${SOURCE_FILE}" || ! -f "${HEADER_DIR}/reachy_llama.h" ]]; then
    printf '%s\n' 'RMA-133 benchmark source/header is missing.' >&2
    exit 1
fi

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
CLANG="${HOST_TAG}/bin/aarch64-linux-android${MIN_SDK}-clang"
READELF="${HOST_TAG}/bin/llvm-readelf"
if [[ ! -x "${CLANG}" || ! -x "${READELF}" ]]; then
    printf '%s\n' 'Pinned NDK ARM64 compiler or readelf is missing.' >&2
    exit 1
fi

rm -rf -- "${OUTPUT_DIR}"
mkdir -p "${OUTPUT_DIR}"
"${CLANG}" \
    -std=c17 \
    -O2 \
    -fPIE \
    -Wall \
    -Wextra \
    -Wpedantic \
    -Wconversion \
    -Wsign-conversion \
    -Wshadow \
    -Werror \
    -I"${HEADER_DIR}" \
    "${SOURCE_FILE}" \
    -L"${RUNTIME_DIR}" \
    -Wl,--no-undefined \
    -Wl,-rpath,\$ORIGIN \
    -lreachy_llama \
    -lm \
    -ldl \
    -o "${OUTPUT_DIR}/rma133_benchmark"

"${READELF}" -d "${OUTPUT_DIR}/rma133_benchmark" > "${OUTPUT_DIR}/rma133_benchmark.dynamic.txt"
if grep -E 'lib(c\+\+|stdc\+\+|llama|ggml)' "${OUTPUT_DIR}/rma133_benchmark.dynamic.txt"; then
    printf '%s\n' 'RMA-133 benchmark has a prohibited direct runtime dependency.' >&2
    exit 1
fi
if ! grep -F 'libreachy_llama.so' "${OUTPUT_DIR}/rma133_benchmark.dynamic.txt" >/dev/null; then
    printf '%s\n' 'RMA-133 benchmark is not linked through the first-party reachy_llama ABI.' >&2
    exit 1
fi
sha256sum "${OUTPUT_DIR}/rma133_benchmark" > "${OUTPUT_DIR}/rma133_benchmark.sha256"
printf 'RMA-133 Android benchmark built: %s\n' "${OUTPUT_DIR}/rma133_benchmark"

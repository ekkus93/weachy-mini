#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
WORK_ROOT="${REACHY_UNITY_WORK_ROOT:-${ROOT_DIR}/build/reachy-unity}"
ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
TOOLCHAIN_LOCK="${ROOT_DIR}/toolchain.lock.json"
MUJOCO_LOCK="${ROOT_DIR}/third_party/mujoco-source.lock.json"
REACHY_LOCK="${ROOT_DIR}/third_party/reachy-mini-source.lock.json"
LLAMA_LOCK="${ROOT_DIR}/third_party/llama-cpp-source.lock.json"

if [[ -z "${ANDROID_SDK_ROOT}" || ! -d "${ANDROID_SDK_ROOT}" ]]; then
    printf '%s\n' 'ANDROID_SDK_ROOT or ANDROID_HOME must identify the Android SDK.' >&2
    exit 1
fi

mapfile -t pins < <(
    python3 - "${TOOLCHAIN_LOCK}" "${MUJOCO_LOCK}" "${REACHY_LOCK}" "${LLAMA_LOCK}" <<'PY'
import json
import sys
from pathlib import Path

toolchain = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
mujoco = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
reachy = json.loads(Path(sys.argv[3]).read_text(encoding="utf-8"))
llama = json.loads(Path(sys.argv[4]).read_text(encoding="utf-8"))
android = toolchain["android"]
for value in (
    android["ndk"],
    android["cmake"],
    android["native_feasibility_min_sdk"],
    mujoco["repository"],
    mujoco["commit"],
    mujoco["version"],
    reachy["output_subdirectory"],
    llama["repository"],
    llama["commit"],
):
    print(value)
PY
)
if (( ${#pins[@]} != 9 )); then
    printf '%s\n' 'Could not read native runtime pins.' >&2
    exit 1
fi

NDK_VERSION="${pins[0]}"
CMAKE_VERSION="${pins[1]}"
ANDROID_MIN_SDK="${pins[2]}"
MUJOCO_REPOSITORY="${pins[3]}"
MUJOCO_COMMIT="${pins[4]}"
MUJOCO_VERSION="${pins[5]}"
REACHY_OUTPUT_SUBDIRECTORY="${pins[6]}"
LLAMA_REPOSITORY="${pins[7]}"
LLAMA_COMMIT="${pins[8]}"
SDKMANAGER="${ANDROID_SDK_ROOT}/cmdline-tools/latest/bin/sdkmanager"
if [[ ! -x "${SDKMANAGER}" ]]; then
    SDKMANAGER="$(command -v sdkmanager || true)"
fi
if [[ -z "${SDKMANAGER}" || ! -x "${SDKMANAGER}" ]]; then
    printf '%s\n' 'Android sdkmanager is unavailable.' >&2
    exit 1
fi

NDK_ROOT="${ANDROID_SDK_ROOT}/ndk/${NDK_VERSION}"
CMAKE_ROOT="${ANDROID_SDK_ROOT}/cmake/${CMAKE_VERSION}"
if [[ ! -f "${NDK_ROOT}/source.properties" || ! -x "${CMAKE_ROOT}/bin/cmake" ]]; then
    "${SDKMANAGER}" --install \
        "ndk;${NDK_VERSION}" \
        "cmake;${CMAKE_VERSION}"
fi
if [[ ! -f "${NDK_ROOT}/source.properties" || ! -x "${CMAKE_ROOT}/bin/ninja" ]]; then
    printf '%s\n' 'The pinned Android NDK or CMake installation is incomplete.' >&2
    exit 1
fi

MUJOCO_SOURCE_DIR="${WORK_ROOT}/upstream/mujoco"
rm -rf -- "${MUJOCO_SOURCE_DIR}"
git init --quiet "${MUJOCO_SOURCE_DIR}"
git -C "${MUJOCO_SOURCE_DIR}" remote add origin "${MUJOCO_REPOSITORY}"
git -C "${MUJOCO_SOURCE_DIR}" fetch --quiet --depth=1 origin "${MUJOCO_COMMIT}"
git -C "${MUJOCO_SOURCE_DIR}" checkout --quiet --detach FETCH_HEAD
python3 "${SCRIPT_DIR}/verify_source_checkout.py" \
    --source "${MUJOCO_SOURCE_DIR}" \
    --lock "${MUJOCO_LOCK}"

LLAMA_SOURCE_DIR="${WORK_ROOT}/upstream/llama.cpp"
rm -rf -- "${LLAMA_SOURCE_DIR}"
git init --quiet "${LLAMA_SOURCE_DIR}"
git -C "${LLAMA_SOURCE_DIR}" remote add origin "${LLAMA_REPOSITORY}"
git -C "${LLAMA_SOURCE_DIR}" fetch --quiet --depth=1 origin "${LLAMA_COMMIT}"
git -C "${LLAMA_SOURCE_DIR}" checkout --quiet --detach FETCH_HEAD
python3 "${SCRIPT_DIR}/verify_source_checkout.py" \
    --source "${LLAMA_SOURCE_DIR}" \
    --lock "${LLAMA_LOCK}"

IMPORTED_MODEL_DIR="${WORK_ROOT}/imported/${REACHY_OUTPUT_SUBDIRECTORY}"
MODEL_XML="${IMPORTED_MODEL_DIR}/reachy_mini.xml"
if [[ ! -s "${MODEL_XML}" ]]; then
    printf 'Imported Reachy model is missing: %s\n' "${MODEL_XML}" >&2
    exit 1
fi

VENV_DIR="${WORK_ROOT}/mujoco-python"
python3 -m venv "${VENV_DIR}"
"${VENV_DIR}/bin/python" -m pip install \
    --disable-pip-version-check \
    --quiet \
    "mujoco==${MUJOCO_VERSION}"

NATIVE_OUTPUT_DIR="${WORK_ROOT}/android-native-arm64"
rm -rf -- "${NATIVE_OUTPUT_DIR}"
PATH="${CMAKE_ROOT}/bin:${PATH}" \
ANDROID_NDK_HOME="${NDK_ROOT}" \
ANDROID_NDK_ROOT="${NDK_ROOT}" \
MUJOCO_SOURCE_DIR="${MUJOCO_SOURCE_DIR}" \
MUJOCO_ANDROID_PLATFORM="android-${ANDROID_MIN_SDK}" \
MUJOCO_ANDROID_BUILD_DIR="${WORK_ROOT}/mujoco-build-arm64" \
MUJOCO_ANDROID_OUTPUT_DIR="${NATIVE_OUTPUT_DIR}" \
REACHY_PROBE_ANDROID_BUILD_DIR="${WORK_ROOT}/reachy-probe-build-arm64" \
    "${SCRIPT_DIR}/build_mujoco_android.sh"

LLAMA_OUTPUT_DIR="${WORK_ROOT}/llama-android-output"
PATH="${CMAKE_ROOT}/bin:${PATH}" \
ANDROID_NDK_HOME="${NDK_ROOT}" \
ANDROID_NDK_ROOT="${NDK_ROOT}" \
LLAMA_CPP_SOURCE_DIR="${LLAMA_SOURCE_DIR}" \
LLAMA_ANDROID_PLATFORM="android-${ANDROID_MIN_SDK}" \
LLAMA_ANDROID_BUILD_DIR="${WORK_ROOT}/llama-build-arm64" \
LLAMA_ANDROID_OUTPUT_DIR="${LLAMA_OUTPUT_DIR}" \
    "${SCRIPT_DIR}/build_llama_android.sh"

PLUGIN_DIR="${ROOT_DIR}/Assets/Plugins/Android/libs/arm64-v8a"
RESOURCE_DIR="${ROOT_DIR}/Assets/Generated/ReachyMini/UnityPresentation/Resources/ReachyMiniRuntime"
rm -rf -- "${PLUGIN_DIR}" "${RESOURCE_DIR}"
mkdir -p "${PLUGIN_DIR}" "${RESOURCE_DIR}"
cp "${NATIVE_OUTPUT_DIR}/libmujoco.so" "${PLUGIN_DIR}/libmujoco.so"
cp "${NATIVE_OUTPUT_DIR}/libreachy_sim.so" "${PLUGIN_DIR}/libreachy_sim.so"
cp "${LLAMA_OUTPUT_DIR}/libreachy_llama.so" "${PLUGIN_DIR}/libreachy_llama.so"

MJB_PATH="${RESOURCE_DIR}/reachy_mini_mjb.bytes"
MANIFEST_PATH="${RESOURCE_DIR}/runtime_manifest_json.bytes"
"${VENV_DIR}/bin/python" "${SCRIPT_DIR}/compile_reachy_runtime_mjb.py" \
    --model "${MODEL_XML}" \
    --output "${MJB_PATH}" \
    --manifest "${MANIFEST_PATH}"

for required_file in \
    "${PLUGIN_DIR}/libmujoco.so" \
    "${PLUGIN_DIR}/libreachy_sim.so" \
    "${PLUGIN_DIR}/libreachy_llama.so" \
    "${MJB_PATH}" \
    "${MANIFEST_PATH}"; do
    if [[ ! -s "${required_file}" ]]; then
        printf 'Staged Unity production runtime file is missing: %s\n' \
            "${required_file}" >&2
        exit 1
    fi
done

printf 'Staged production Unity Android runtime: plugins=%s resources=%s\n' \
    "${PLUGIN_DIR}" "${RESOURCE_DIR}"

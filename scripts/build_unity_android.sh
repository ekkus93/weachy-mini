#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
MODE="${1:-}"
UNITY_EDITOR="${UNITY_EDITOR:-}"
PINNED_UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "${ROOT_DIR}/ProjectSettings/ProjectVersion.txt")"
ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"

if [[ -z "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR must point to Unity %s.\n' "${PINNED_UNITY_VERSION}" >&2
    exit 1
fi

if [[ ! -x "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR is not executable: %s\n' "${UNITY_EDITOR}" >&2
    exit 1
fi

if [[ -z "${ANDROID_SDK_ROOT}" ]]; then
    printf '%s\n' 'ANDROID_SDK_ROOT or ANDROID_HOME must identify the Android SDK.' >&2
    exit 1
fi

if [[ ! -d "${ANDROID_SDK_ROOT}" ]]; then
    printf 'Android SDK directory does not exist: %s\n' "${ANDROID_SDK_ROOT}" >&2
    exit 1
fi

export ANDROID_SDK_ROOT
export ANDROID_HOME="${ANDROID_SDK_ROOT}"

mapfile -t ANDROID_PINS < <(
    python3 - "${ROOT_DIR}/toolchain.lock.json" <<'PY'
import json
from pathlib import Path
import sys

manifest = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
android = manifest["android"]
print(android["compile_sdk"])
print(android["build_tools"])
PY
)

if [[ "${#ANDROID_PINS[@]}" -ne 2 ]]; then
    printf '%s\n' 'Could not read Android SDK pins from toolchain.lock.json.' >&2
    exit 1
fi

COMPILE_SDK="${ANDROID_PINS[0]}"
BUILD_TOOLS="${ANDROID_PINS[1]}"
REQUIRED_PLATFORM="${ANDROID_SDK_ROOT}/platforms/android-${COMPILE_SDK}/android.jar"
REQUIRED_BUILD_TOOLS="${ANDROID_SDK_ROOT}/build-tools/${BUILD_TOOLS}"

SDKMANAGER="${ANDROID_SDK_ROOT}/cmdline-tools/latest/bin/sdkmanager"
if [[ ! -x "${SDKMANAGER}" ]]; then
    mapfile -t SDKMANAGER_CANDIDATES < <(
        find "${ANDROID_SDK_ROOT}/cmdline-tools" \
            -mindepth 3 \
            -maxdepth 3 \
            -type f \
            -path '*/bin/sdkmanager' \
            -perm -u+x \
            -print 2>/dev/null \
            | sort -V
    )
    if [[ "${#SDKMANAGER_CANDIDATES[@]}" -eq 0 ]]; then
        printf 'Android sdkmanager was not found under %s/cmdline-tools.\n' \
            "${ANDROID_SDK_ROOT}" >&2
        exit 1
    fi
    SDKMANAGER="${SDKMANAGER_CANDIDATES[-1]}"
fi

if [[ ! -f "${REQUIRED_PLATFORM}" || ! -d "${REQUIRED_BUILD_TOOLS}" ]]; then
    printf 'Provisioning Android platform %s and Build Tools %s in %s.\n' \
        "${COMPILE_SDK}" "${BUILD_TOOLS}" "${ANDROID_SDK_ROOT}"

    set +o pipefail
    yes | "${SDKMANAGER}" \
        --sdk_root="${ANDROID_SDK_ROOT}" \
        --licenses >/dev/null
    LICENSE_STATUS="${PIPESTATUS[1]}"
    set -o pipefail
    if [[ "${LICENSE_STATUS}" -ne 0 ]]; then
        printf 'Android SDK license acceptance failed with status %s.\n' \
            "${LICENSE_STATUS}" >&2
        exit "${LICENSE_STATUS}"
    fi

    "${SDKMANAGER}" \
        --sdk_root="${ANDROID_SDK_ROOT}" \
        --channel=3 \
        --install \
        "platforms;android-${COMPILE_SDK}" \
        "build-tools;${BUILD_TOOLS}"
fi

if [[ ! -f "${REQUIRED_PLATFORM}" ]]; then
    printf 'Android platform %s was not installed: %s\n' \
        "${COMPILE_SDK}" "${REQUIRED_PLATFORM}" >&2
    exit 1
fi

if [[ ! -x "${REQUIRED_BUILD_TOOLS}/aapt2" ]]; then
    printf 'Android Build Tools %s are incomplete: %s\n' \
        "${BUILD_TOOLS}" "${REQUIRED_BUILD_TOOLS}" >&2
    exit 1
fi

printf 'android_sdk=%s\n' "${ANDROID_SDK_ROOT}"
printf 'android_platform=%s\n' "${COMPILE_SDK}"
printf 'android_build_tools=%s\n' "${BUILD_TOOLS}"

case "${MODE}" in
    development)
        METHOD='ReachyMini.Editor.AndroidBuild.BuildDevelopmentApk'
        ;;
    device-feasibility)
        METHOD='ReachyMini.Editor.AndroidBuild.BuildDeviceFeasibilityApk'
        ;;
    release)
        METHOD='ReachyMini.Editor.AndroidBuild.BuildReleaseAab'
        ;;
    *)
        printf '%s\n' \
            'usage: build_unity_android.sh development|device-feasibility|release' >&2
        exit 2
        ;;
esac

"${UNITY_EDITOR}" \
    -batchmode \
    -nographics \
    -quit \
    -projectPath "${ROOT_DIR}" \
    -executeMethod "${METHOD}" \
    -logFile -

#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
MODE="${1:-}"
UNITY_EDITOR="${UNITY_EDITOR:-}"
PINNED_UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "${ROOT_DIR}/ProjectSettings/ProjectVersion.txt")"

if [[ -z "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR must point to Unity %s.\n' "${PINNED_UNITY_VERSION}" >&2
    exit 1
fi

if [[ ! -x "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR is not executable: %s\n' "${UNITY_EDITOR}" >&2
    exit 1
fi

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
n    *)
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

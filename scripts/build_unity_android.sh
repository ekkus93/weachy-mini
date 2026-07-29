#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
MODE="${1:-}"
UNITY_EDITOR="${UNITY_EDITOR:-}"

if [[ -z "${UNITY_EDITOR}" ]]; then
    printf '%s\n' 'UNITY_EDITOR must point to Unity 6000.3.18f1.' >&2
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
    release)
        METHOD='ReachyMini.Editor.AndroidBuild.BuildReleaseAab'
        ;;
    *)
        printf '%s\n' 'usage: build_unity_android.sh development|release' >&2
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

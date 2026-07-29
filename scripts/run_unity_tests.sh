#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
UNITY_EDITOR="${UNITY_EDITOR:-}"
RESULTS_DIR="${UNITY_TEST_RESULTS_DIR:-${ROOT_DIR}/test-results/unity}"

if [[ -z "${UNITY_EDITOR}" ]]; then
    printf '%s\n' 'UNITY_EDITOR must point to Unity 6000.3.18f1.' >&2
    exit 1
fi

if [[ ! -x "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR is not executable: %s\n' "${UNITY_EDITOR}" >&2
    exit 1
fi

mkdir -p "${RESULTS_DIR}"

run_tests()
{
    local platform="$1"
    local result_path="${RESULTS_DIR}/${platform}.xml"
    local log_path="${RESULTS_DIR}/${platform}.log"

    "${UNITY_EDITOR}" \
        -batchmode \
        -nographics \
        -quit \
        -projectPath "${ROOT_DIR}" \
        -runTests \
        -testPlatform "${platform}" \
        -testResults "${result_path}" \
        -logFile "${log_path}"

    if [[ ! -s "${result_path}" ]]; then
        printf 'Unity %s test run did not produce %s.\n' \
            "${platform}" "${result_path}" >&2
        if [[ -f "${log_path}" ]]; then
            cat "${log_path}" >&2
        fi
        exit 1
    fi
}

run_tests editmode
run_tests playmode

printf 'Unity test results written to %s\n' "${RESULTS_DIR}"

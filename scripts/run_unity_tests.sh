#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
UNITY_EDITOR="${UNITY_EDITOR:-}"
RESULTS_DIR="${UNITY_TEST_RESULTS_DIR:-${ROOT_DIR}/test-results/unity}"
PINNED_UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "${ROOT_DIR}/ProjectSettings/ProjectVersion.txt")"

if [[ -z "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR must point to Unity %s.\n' "${PINNED_UNITY_VERSION}" >&2
    exit 1
fi

if [[ ! -x "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR is not executable: %s\n' "${UNITY_EDITOR}" >&2
    exit 1
fi

mkdir -p "${RESULTS_DIR}"

validate_results()
{
    local result_path="$1"
    local platform="$2"

    python3 - "${result_path}" "${platform}" <<'PY'
from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

result_path = Path(sys.argv[1])
platform = sys.argv[2]

try:
    root = ET.parse(result_path).getroot()
except (OSError, ET.ParseError) as exc:
    raise SystemExit(f"Unity {platform} results are unreadable: {exc}") from exc

if root.tag != "test-run":
    raise SystemExit(
        f"Unity {platform} results have unexpected root element {root.tag!r}."
    )

try:
    total = int(root.attrib["total"])
    failed = int(root.attrib["failed"])
    errors = int(root.attrib.get("errors", "0"))
except (KeyError, ValueError) as exc:
    raise SystemExit(
        f"Unity {platform} results are missing valid NUnit counters: {root.attrib}"
    ) from exc

if total <= 0:
    raise SystemExit(f"Unity {platform} discovered no tests.")
if failed != 0 or errors != 0:
    raise SystemExit(
        f"Unity {platform} reported failed={failed}, errors={errors}, total={total}."
    )
if root.attrib.get("result") != "Passed":
    raise SystemExit(
        f"Unity {platform} result is {root.attrib.get('result')!r}, not 'Passed'."
    )

print(f"Unity {platform}: {total} tests passed.")
PY
}

run_tests()
{
    local file_stem="$1"
    local platform="$2"
    local result_path="${RESULTS_DIR}/${file_stem}.xml"
    local log_path="${RESULTS_DIR}/${file_stem}.log"
    local unity_status

    rm -f -- "${result_path}" "${log_path}"

    set +e
    "${UNITY_EDITOR}" \
        -batchmode \
        -nographics \
        -projectPath "${ROOT_DIR}" \
        -runTests \
        -testPlatform "${platform}" \
        -testResults "${result_path}" \
        -logFile "${log_path}"
    unity_status=$?
    set -e

    if [[ ! -s "${result_path}" ]]; then
        printf 'Unity %s test run did not produce %s.\n' \
            "${platform}" "${result_path}" >&2
        if [[ -f "${log_path}" ]]; then
            cat "${log_path}" >&2
        fi
        exit 1
    fi

    if ! validate_results "${result_path}" "${platform}"; then
        if [[ -f "${log_path}" ]]; then
            cat "${log_path}" >&2
        fi
        exit 1
    fi

    if (( unity_status != 0 )); then
        printf 'Unity %s exited with status %s despite producing passing results.\n' \
            "${platform}" "${unity_status}" >&2
        if [[ -f "${log_path}" ]]; then
            cat "${log_path}" >&2
        fi
        exit "${unity_status}"
    fi
}

run_tests editmode EditMode
run_tests playmode PlayMode

printf 'Unity test results written to %s\n' "${RESULTS_DIR}"

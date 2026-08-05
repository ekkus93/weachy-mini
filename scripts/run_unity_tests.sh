#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
UNITY_EDITOR="${UNITY_EDITOR:-}"
RESULTS_DIR="${UNITY_TEST_RESULTS_DIR:-${ROOT_DIR}/test-results/unity}"
GRAPHICS_MODE="${UNITY_TEST_GRAPHICS_MODE:-real}"
GRAPHICS_API="${UNITY_TEST_GRAPHICS_API:-glcore}"
DISPLAY_OVERRIDE="${UNITY_TEST_DISPLAY:-}"
PINNED_UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "${ROOT_DIR}/ProjectSettings/ProjectVersion.txt")"

declare -a UNITY_LAUNCHER=()
declare -a UNITY_GRAPHICS_ARGS=()

if [[ -z "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR must point to Unity %s.\n' "${PINNED_UNITY_VERSION}" >&2
    exit 1
fi

if [[ ! -x "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR is not executable: %s\n' "${UNITY_EDITOR}" >&2
    exit 1
fi

configure_graphics()
{
    if [[ "${GRAPHICS_MODE}" != "real" ]]; then
        printf 'Unsupported UNITY_TEST_GRAPHICS_MODE=%s; GPU tests require real graphics.\n' \
            "${GRAPHICS_MODE}" >&2
        exit 1
    fi

    case "${GRAPHICS_API}" in
        glcore)
            UNITY_GRAPHICS_ARGS=(-force-glcore)
            ;;
        vulkan)
            UNITY_GRAPHICS_ARGS=(-force-vulkan)
            ;;
        auto)
            UNITY_GRAPHICS_ARGS=()
            ;;
        *)
            printf 'Unsupported UNITY_TEST_GRAPHICS_API=%s; use glcore, vulkan, or auto.\n' \
                "${GRAPHICS_API}" >&2
            exit 1
            ;;
    esac

    if [[ -n "${DISPLAY_OVERRIDE}" ]]; then
        export DISPLAY="${DISPLAY_OVERRIDE}"
    fi

    if [[ -n "${DISPLAY:-}" ]]; then
        printf 'Unity tests will use DISPLAY=%s.\n' "${DISPLAY}"
        return
    fi

    if command -v xvfb-run >/dev/null 2>&1; then
        UNITY_LAUNCHER=(
            xvfb-run
            -a
            -s
            '-screen 0 1280x720x24'
        )
        printf '%s\n' 'Unity tests will use xvfb-run with a real graphics device.'
        return
    fi

    if [[ -S /tmp/.X11-unix/X0 ]]; then
        export DISPLAY=:0
        printf '%s\n' 'Unity tests will use the detected X11 display at :0.'
        return
    fi

    printf '%s\n' \
        'GPU Unity tests require DISPLAY, UNITY_TEST_DISPLAY, or xvfb-run; refusing NullGfxDevice.' \
        >&2
    exit 1
}

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

validate_graphics_log()
{
    local log_path="$1"
    local platform="$2"

    if grep -Eq 'NullGfxDevice|Renderer:[[:space:]]*Null Device' "${log_path}"; then
        printf 'Unity %s initialized NullGfxDevice; GPU test evidence is invalid.\n' \
            "${platform}" >&2
        return 1
    fi
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
    "${UNITY_LAUNCHER[@]}" \
        "${UNITY_EDITOR}" \
        -batchmode \
        "${UNITY_GRAPHICS_ARGS[@]}" \
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

    if ! validate_graphics_log "${log_path}" "${platform}"; then
        cat "${log_path}" >&2
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

mkdir -p "${RESULTS_DIR}"
configure_graphics
run_tests editmode EditMode
run_tests playmode PlayMode

printf 'Unity test results written to %s\n' "${RESULTS_DIR}"

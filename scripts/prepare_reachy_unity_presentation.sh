#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
LOCK_PATH="${ROOT_DIR}/third_party/reachy-mini-source.lock.json"
UNITY_EDITOR="${UNITY_EDITOR:-}"
WORK_ROOT="${REACHY_UNITY_WORK_ROOT:-${ROOT_DIR}/build/reachy-unity}"
SOURCE_CHECKOUT="${REACHY_SOURCE_CHECKOUT:-${WORK_ROOT}/upstream/reachy-mini}"
IMPORT_ROOT="${WORK_ROOT}/imported"
RENDER_ROOT="${WORK_ROOT}/render"
LOG_PATH="${WORK_ROOT}/unity-presentation-builder.log"

if [[ -z "${UNITY_EDITOR}" ]]; then
    printf '%s\n' 'UNITY_EDITOR must identify the pinned Unity editor.' >&2
    exit 1
fi
if [[ ! -x "${UNITY_EDITOR}" ]]; then
    printf 'UNITY_EDITOR is not executable: %s\n' "${UNITY_EDITOR}" >&2
    exit 1
fi
if [[ ! -f "${LOCK_PATH}" ]]; then
    printf 'Reachy source lock is missing: %s\n' "${LOCK_PATH}" >&2
    exit 1
fi

mapfile -t reachy_pin < <(
    python3 - "${LOCK_PATH}" <<'PY'
import json
import sys
from pathlib import Path, PurePosixPath

lock = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
values = (
    lock["repository"],
    lock["commit"],
    str(PurePosixPath(lock["model_file"]).parent),
    lock["license_file"],
    lock["output_subdirectory"],
)
if any(not isinstance(value, str) or not value or "\n" in value for value in values):
    raise SystemExit("Reachy source lock contains an invalid string field")
for value in values:
    print(value)
PY
)
if (( ${#reachy_pin[@]} != 5 )); then
    printf '%s\n' 'Reachy source lock did not produce five required fields.' >&2
    exit 1
fi

REPOSITORY="${reachy_pin[0]}"
COMMIT="${reachy_pin[1]}"
MODEL_DIRECTORY="${reachy_pin[2]}"
LICENSE_FILE="${reachy_pin[3]}"
OUTPUT_SUBDIRECTORY="${reachy_pin[4]}"

prepare_checkout()
{
    local checkout="$1"
    local sparse_file

    rm -rf -- "${checkout}"
    mkdir -p "${checkout}"
    GIT_TERMINAL_PROMPT=0 git -C "${checkout}" init --quiet
    GIT_TERMINAL_PROMPT=0 git -C "${checkout}" remote add origin "${REPOSITORY}"
    GIT_TERMINAL_PROMPT=0 git -C "${checkout}" config core.sparseCheckout true
    sparse_file="${checkout}/.git/info/sparse-checkout"
    mkdir -p "$(dirname -- "${sparse_file}")"
    printf '/%s\n/%s/\n' "${LICENSE_FILE}" "${MODEL_DIRECTORY}" > "${sparse_file}"
    GIT_TERMINAL_PROMPT=0 git -C "${checkout}" fetch \
        --quiet \
        --depth=1 \
        origin \
        "${COMMIT}"
    GIT_TERMINAL_PROMPT=0 git -C "${checkout}" checkout \
        --quiet \
        --detach \
        FETCH_HEAD
    GIT_TERMINAL_PROMPT=0 git -C "${checkout}" lfs install --local >/dev/null
    GIT_TERMINAL_PROMPT=0 git -C "${checkout}" lfs pull \
        --include="${MODEL_DIRECTORY}/**" \
        --exclude=""
}

mkdir -p "${WORK_ROOT}"
if [[ -z "${REACHY_SOURCE_CHECKOUT:-}" ]]; then
    prepare_checkout "${SOURCE_CHECKOUT}"
fi

rm -rf -- "${IMPORT_ROOT}" "${RENDER_ROOT}"
python3 "${SCRIPT_DIR}/import_reachy_assets.py" \
    --source "${SOURCE_CHECKOUT}" \
    --lock "${LOCK_PATH}" \
    --output-root "${IMPORT_ROOT}"
python3 "${SCRIPT_DIR}/prepare_reachy_unity_assets.py" \
    --source "${IMPORT_ROOT}/${OUTPUT_SUBDIRECTORY}" \
    --output "${RENDER_ROOT}"

MODEL_MAP_PATH="${IMPORT_ROOT}/${OUTPUT_SUBDIRECTORY}/MODEL_MAP.json"
if [[ ! -s "${MODEL_MAP_PATH}" ]]; then
    printf 'Imported Reachy model map is missing or empty: %s\n' \
        "${MODEL_MAP_PATH}" >&2
    exit 1
fi

rm -f -- "${LOG_PATH}"
set +e
REACHY_UNITY_RENDER_ROOT="${RENDER_ROOT}" \
REACHY_MODEL_MAP_PATH="${MODEL_MAP_PATH}" \
    "${UNITY_EDITOR}" \
    -batchmode \
    -nographics \
    -quit \
    -projectPath "${ROOT_DIR}" \
    -executeMethod ReachyMini.Editor.ReachyPresentationPipeline.BuildFromCommandLine \
    -logFile "${LOG_PATH}"
unity_status=$?
set -e

if (( unity_status != 0 )); then
    printf 'Unity presentation generation failed with status %s.\n' \
        "${unity_status}" >&2
    if [[ -f "${LOG_PATH}" ]]; then
        cat "${LOG_PATH}" >&2
    fi
    exit "${unity_status}"
fi

if grep -E 'Assets/ReachyMini/.*warning CS[0-9]+' "${LOG_PATH}" >&2; then
    printf '%s\n' 'Unity presentation generation emitted first-party compiler warnings.' >&2
    exit 1
fi

PREFAB_PATH="${ROOT_DIR}/Assets/Generated/ReachyMini/UnityPresentation/Resources/ReachyMiniPresentation.prefab"
SCENE_PATH="${ROOT_DIR}/Assets/Generated/ReachyMini/UnityPresentation/ReachyMiniPresentation.unity"
if [[ ! -s "${PREFAB_PATH}" ]]; then
    printf 'Generated Reachy prefab is missing or empty: %s\n' "${PREFAB_PATH}" >&2
    exit 1
fi
if [[ ! -s "${SCENE_PATH}" ]]; then
    printf 'Generated Reachy presentation scene is missing or empty: %s\n' \
        "${SCENE_PATH}" >&2
    exit 1
fi

printf 'Reachy Unity presentation prepared: prefab=%s scene=%s\n' \
    "${PREFAB_PATH}" "${SCENE_PATH}"

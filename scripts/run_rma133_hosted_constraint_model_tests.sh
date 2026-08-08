#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
LOCK_FILE="${ROOT_DIR}/third_party/llama-cpp-test-model.lock.json"
BUILD_DIR="${RMA130_HOST_BUILD_DIR:-${ROOT_DIR}/build/rma133-abi2-host}"
CACHE_ROOT="${RUNNER_TEMP:-${ROOT_DIR}/build}/rma133-test-model"
mkdir -p "${CACHE_ROOT}"

for command in curl python3 sha256sum stat; do
    if ! command -v "${command}" >/dev/null; then
        printf 'RMA-133 hosted model test requires command: %s\n' "${command}" >&2
        exit 1
    fi
done

mapfile -t pin < <(
    python3 - "${LOCK_FILE}" <<'PY'
import json
import sys
from pathlib import Path

lock = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
required = {
    "schema_version": 1,
    "source_revision": "def3e2dd70df35ecbf6403ea347de4c5977220c1",
    "filename": "stories260K.gguf",
    "file_size_bytes": 1185376,
    "sha256": "047bf46455a544931cff6fef14d7910154c56afbc23ab1c5e56a72e69912c04b",
}
for key, expected in required.items():
    if lock.get(key) != expected:
        raise SystemExit(f"RMA-133 test-model lock changed: {key}")
url = lock.get("url")
if not isinstance(url, str) or not url.startswith("https://"):
    raise SystemExit("RMA-133 test-model URL must be HTTPS")
if required["source_revision"] not in url or required["filename"] not in url:
    raise SystemExit("RMA-133 test-model URL is not revision/file pinned")
print(url)
print(required["filename"])
print(required["file_size_bytes"])
print(required["sha256"])
PY
)
if (( ${#pin[@]} != 4 )); then
    printf '%s\n' 'RMA-133 could not read the frozen hosted test-model lock.' >&2
    exit 1
fi

url="${pin[0]}"
filename="${pin[1]}"
expected_size="${pin[2]}"
expected_sha="${pin[3]}"
model_path="${CACHE_ROOT}/${expected_sha}-${filename}"

valid_cache=false
if [[ -f "${model_path}" ]]; then
    actual_size="$(stat -c '%s' "${model_path}")"
    actual_sha="$(sha256sum "${model_path}" | awk '{print $1}')"
    if [[ "${actual_size}" == "${expected_size}" && "${actual_sha}" == "${expected_sha}" ]]; then
        valid_cache=true
    else
        rm -f -- "${model_path}"
    fi
fi

if [[ "${valid_cache}" != true ]]; then
    partial="${model_path}.partial.$$"
    rm -f -- "${partial}"
    curl --fail-with-body --location --proto '=https' --tlsv1.2 \
        --retry 2 --retry-all-errors --output "${partial}" "${url}"
    actual_size="$(stat -c '%s' "${partial}")"
    actual_sha="$(sha256sum "${partial}" | awk '{print $1}')"
    if [[ "${actual_size}" != "${expected_size}" || "${actual_sha}" != "${expected_sha}" ]]; then
        printf 'RMA-133 hosted test-model integrity failure: size=%s sha256=%s\n' \
            "${actual_size}" "${actual_sha}" >&2
        rm -f -- "${partial}"
        exit 1
    fi
    mv -- "${partial}" "${model_path}"
fi

cmake --build "${BUILD_DIR}" --target reachy_llama_constraint_model_tests --parallel
binary="${BUILD_DIR}/native/llama_runtime/reachy_llama_constraint_model_tests"
if [[ ! -x "${binary}" ]]; then
    printf 'RMA-133 loaded-model constraint test binary is missing: %s\n' "${binary}" >&2
    exit 1
fi

"${binary}" "${model_path}"
printf 'RMA-133 loaded-model constrained lifecycle tests passed with pinned fixture %s.\n' \
    "${expected_sha}"

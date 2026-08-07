#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
SOURCE_DIR="${LLAMA_CPP_SOURCE_DIR:-${1:-}}"
LOCK_FILE="${ROOT_DIR}/third_party/llama-cpp-source.lock.json"
BUILD_DIR="${RMA130_HOST_BUILD_DIR:-${ROOT_DIR}/build/rma130-llama-host}"

if [[ -z "${SOURCE_DIR}" ]]; then
    printf '%s\n' "usage: LLAMA_CPP_SOURCE_DIR=/path/to/llama.cpp $0" >&2
    exit 2
fi

python3 "${SCRIPT_DIR}/verify_source_checkout.py" \
    --source "${SOURCE_DIR}" \
    --lock "${LOCK_FILE}"

python3 - "${SOURCE_DIR}" "${LOCK_FILE}" <<'PY'
import json
import subprocess
import sys
from pathlib import Path

source = Path(sys.argv[1]).resolve()
lock = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
license_path = source / lock["license_file"]
if not license_path.is_file():
    raise SystemExit(f"Pinned llama.cpp license is missing: {license_path}")
actual_blob = subprocess.check_output(
    ["git", "-C", str(source), "hash-object", str(license_path)], text=True
).strip()
if actual_blob != lock["license_git_blob"]:
    raise SystemExit(
        f"llama.cpp license blob mismatch: expected {lock['license_git_blob']}, found {actual_blob}"
    )
PY

rm -rf -- "${BUILD_DIR}"
cmake \
    -S "${ROOT_DIR}" \
    -B "${BUILD_DIR}" \
    -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_TESTING=ON \
    -DREACHY_ENABLE_SANITIZERS="${REACHY_ENABLE_SANITIZERS:-OFF}" \
    -DREACHY_BUILD_LLAMA_RUNTIME=ON \
    -DREACHY_LLAMA_CPP_SOURCE_DIR="${SOURCE_DIR}"
cmake --build "${BUILD_DIR}" --target reachy_llama_contract_tests --parallel
ctest --test-dir "${BUILD_DIR}" --output-on-failure -R '^reachy_llama_contracts$'

printf 'RMA-130 hosted native contracts passed for %s\n' \
    "$(git -C "${SOURCE_DIR}" rev-parse HEAD)"

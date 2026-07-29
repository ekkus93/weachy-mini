#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
MODE="${1:-all}"

python3 "${SCRIPT_DIR}/verify_toolchain.py" --manifest-only
python3 "${SCRIPT_DIR}/validate_scaffold.py"
python3 "${SCRIPT_DIR}/check_docs_links.py"
python3 "${SCRIPT_DIR}/validate_inventory.py"
python3 -m compileall -q "${SCRIPT_DIR}"

if [[ "${MODE}" == '--static-only' ]]; then
    exit 0
fi

if [[ "${MODE}" != 'all' ]]; then
    printf '%s\n' 'usage: ci.sh [--static-only]' >&2
    exit 2
fi

"${SCRIPT_DIR}/build_native.sh"

dotnet restore "${ROOT_DIR}/managed/ReachyMini.Core.Tests/ReachyMini.Core.Tests.csproj"
dotnet run \
    --project "${ROOT_DIR}/managed/ReachyMini.Core.Tests/ReachyMini.Core.Tests.csproj" \
    --configuration Release \
    --no-restore

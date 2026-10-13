#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
export RMA190_REMOTE_OLLAMA_DIAGNOSTIC=1
exec bash "${SCRIPT_DIR}/run_rma190_offline_speech_end_to_end_android.sh" "$@"

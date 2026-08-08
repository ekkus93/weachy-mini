#!/usr/bin/env bash
set -euo pipefail
exec python3 "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/run_rma133_device_benchmark_v6.py"

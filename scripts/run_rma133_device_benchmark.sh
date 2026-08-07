#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
CONFIG="${RMA133_CONFIG:-${ROOT_DIR}/benchmarks/rma133/candidates.json}"
CASES="${RMA133_CASES:-${ROOT_DIR}/benchmarks/rma133/behavior_cases.tsv}"
SYSTEM_PROMPT="${RMA133_SYSTEM_PROMPT:-${ROOT_DIR}/benchmarks/rma133/system_prompt.txt}"
RUNTIME_DIR="${RMA133_RUNTIME_DIR:-${ROOT_DIR}/build/rma133/runtime}"
BENCHMARK_DIR="${RMA133_BENCHMARK_OUTPUT_DIR:-${ROOT_DIR}/build/rma133/benchmark}"
RESULTS_DIR="${RMA133_RESULTS_DIR:-${ROOT_DIR}/build/rma133/results}"
MODEL_CACHE_DIR="${RMA133_MODEL_CACHE_DIR:-${HOME}/.cache/weachy-mini/rma133/models}"
DEVICE_SERIAL="${RMA133_DEVICE_SERIAL:-}"
REMOTE_ROOT="/data/local/tmp/reachy-rma133-${GITHUB_RUN_ID:-manual}-$$"

for command in adb curl python3 sha256sum stat; do
    if ! command -v "${command}" >/dev/null; then
        printf 'RMA-133 required command is missing: %s\n' "${command}" >&2
        exit 1
    fi
done
for path in \
    "${CONFIG}" \
    "${CASES}" \
    "${SYSTEM_PROMPT}" \
    "${RUNTIME_DIR}/libreachy_llama.so" \
    "${BENCHMARK_DIR}/rma133_benchmark"; do
    if [[ ! -s "${path}" ]]; then
        printf 'RMA-133 required input is missing or empty: %s\n' "${path}" >&2
        exit 1
    fi
done

python3 "${SCRIPT_DIR}/score_rma133_benchmark.py" validate --config "${CONFIG}" >/dev/null

if [[ -z "${DEVICE_SERIAL}" ]]; then
    mapfile -t devices < <(adb devices | awk 'NR > 1 && $2 == "device" {print $1}')
    if (( ${#devices[@]} != 1 )); then
        printf 'RMA-133 requires exactly one authorized Android device; found %d.\n' "${#devices[@]}" >&2
        exit 1
    fi
    DEVICE_SERIAL="${devices[0]}"
fi
ADB=(adb -s "${DEVICE_SERIAL}")
if [[ "$("${ADB[@]}" get-state)" != "device" ]]; then
    printf 'RMA-133 Android device is not ready: %s\n' "${DEVICE_SERIAL}" >&2
    exit 1
fi
DEVICE_ABI="$("${ADB[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
DEVICE_API="$("${ADB[@]}" shell getprop ro.build.version.sdk | tr -d '\r')"
DEVICE_QEMU="$("${ADB[@]}" shell getprop ro.kernel.qemu | tr -d '\r')"
DEVICE_MODEL="$("${ADB[@]}" shell getprop ro.product.model | tr -d '\r')"
if [[ "${DEVICE_ABI}" != "arm64-v8a" || ! "${DEVICE_API}" =~ ^[0-9]+$ || "${DEVICE_API}" -lt 26 ]]; then
    printf 'RMA-133 requires physical ARM64 Android API 26+; found ABI=%s API=%s.\n' \
        "${DEVICE_ABI}" "${DEVICE_API}" >&2
    exit 1
fi
if [[ "${DEVICE_QEMU}" == "1" ]]; then
    printf '%s\n' 'RMA-133 thermal/performance evidence requires a physical device, not an emulator.' >&2
    exit 1
fi

mkdir -p "${RESULTS_DIR}" "${MODEL_CACHE_DIR}"
rm -rf -- "${RESULTS_DIR:?}"/*
cleanup() {
    "${ADB[@]}" shell rm -rf -- "${REMOTE_ROOT}" >/dev/null 2>&1 || true
}
trap cleanup EXIT
"${ADB[@]}" shell mkdir -p "${REMOTE_ROOT}"
"${ADB[@]}" push "${RUNTIME_DIR}/libreachy_llama.so" "${REMOTE_ROOT}/libreachy_llama.so" >/dev/null
"${ADB[@]}" push "${BENCHMARK_DIR}/rma133_benchmark" "${REMOTE_ROOT}/rma133_benchmark" >/dev/null
"${ADB[@]}" push "${CASES}" "${REMOTE_ROOT}/behavior_cases.tsv" >/dev/null
"${ADB[@]}" push "${SYSTEM_PROMPT}" "${REMOTE_ROOT}/system_prompt.txt" >/dev/null
"${ADB[@]}" shell chmod 0755 "${REMOTE_ROOT}/rma133_benchmark"

python3 - "${CONFIG}" > "${RESULTS_DIR}/candidate_rows.tsv" <<'PY'
import json
import sys
from pathlib import Path

config = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
profile = config["runtime_profile"]
thermal = config["selection_policy"]["maximum_battery_temperature_c"]
for candidate in config["candidates"]:
    artifact = candidate["artifact"]
    values = [
        candidate["candidate_id"],
        artifact["url"],
        artifact["filename"],
        str(artifact["file_size_bytes"]),
        artifact["sha256"],
        candidate["user_prompt_suffix"] or "-",
        str(profile["context_tokens"]),
        str(profile["batch_tokens"]),
        str(profile["micro_batch_tokens"]),
        str(profile["max_generated_tokens"]),
        str(profile["threads"]),
        str(profile["batch_threads"]),
        str(profile["temperature"]),
        str(profile["min_p"]),
        str(profile["seed"]),
        str(profile["stream_queue_capacity"]),
        str(thermal),
    ]
    if any("\t" in value or "\n" in value for value in values):
        raise SystemExit("RMA-133 candidate fields must be single-line TSV-safe values")
    print("\t".join(values))
PY

printf 'RMA-133 device: serial=%s model=%s ABI=%s API=%s\n' \
    "${DEVICE_SERIAL}" "${DEVICE_MODEL}" "${DEVICE_ABI}" "${DEVICE_API}" \
    | tee "${RESULTS_DIR}/device.txt"

report_args=()
while IFS=$'\t' read -r \
    candidate_id artifact_url artifact_filename expected_size expected_sha suffix \
    context batch ubatch max_gen threads batch_threads temperature min_p seed queue thermal_abort; do
    if [[ -z "${candidate_id}" ]]; then
        continue
    fi
    cache_path="${MODEL_CACHE_DIR}/${expected_sha}-${artifact_filename}"
    valid_cache=false
    if [[ -f "${cache_path}" ]]; then
        actual_size="$(stat -c '%s' "${cache_path}")"
        actual_sha="$(sha256sum "${cache_path}" | awk '{print $1}')"
        if [[ "${actual_size}" == "${expected_size}" && "${actual_sha}" == "${expected_sha}" ]]; then
            valid_cache=true
            printf 'RMA-133 verified cached artifact for %s.\n' "${candidate_id}"
        else
            printf 'RMA-133 deleting invalid cached artifact for %s.\n' "${candidate_id}" >&2
            rm -f -- "${cache_path}"
        fi
    fi
    if [[ "${valid_cache}" != true ]]; then
        tmp_path="${cache_path}.partial.$$"
        rm -f -- "${tmp_path}"
        printf 'RMA-133 downloading exact pinned artifact for %s.\n' "${candidate_id}"
        curl \
            --fail-with-body \
            --location \
            --proto '=https' \
            --tlsv1.2 \
            --retry 2 \
            --retry-all-errors \
            --output "${tmp_path}" \
            "${artifact_url}"
        actual_size="$(stat -c '%s' "${tmp_path}")"
        actual_sha="$(sha256sum "${tmp_path}" | awk '{print $1}')"
        if [[ "${actual_size}" != "${expected_size}" || "${actual_sha}" != "${expected_sha}" ]]; then
            printf 'RMA-133 artifact integrity failure for %s: size=%s sha256=%s.\n' \
                "${candidate_id}" "${actual_size}" "${actual_sha}" >&2
            rm -f -- "${tmp_path}"
            exit 1
        fi
        mv -- "${tmp_path}" "${cache_path}"
    fi

    free_kib="$("${ADB[@]}" shell df -Pk /data/local/tmp | awk 'END {print $4}' | tr -d '\r')"
    if [[ ! "${free_kib}" =~ ^[0-9]+$ ]]; then
        printf '%s\n' 'RMA-133 could not determine free device storage.' >&2
        exit 1
    fi
    required_kib="$(( (expected_size + 1023) / 1024 + 262144 ))"
    if (( free_kib < required_kib )); then
        printf 'RMA-133 device storage is insufficient for %s: need %d KiB, have %d KiB.\n' \
            "${candidate_id}" "${required_kib}" "${free_kib}" >&2
        exit 1
    fi

    "${ADB[@]}" shell rm -f -- "${REMOTE_ROOT}/model.gguf"
    "${ADB[@]}" push "${cache_path}" "${REMOTE_ROOT}/model.gguf" >/dev/null
    remote_size="$("${ADB[@]}" shell stat -c '%s' "${REMOTE_ROOT}/model.gguf" | tr -d '\r')"
    if [[ "${remote_size}" != "${expected_size}" ]]; then
        printf 'RMA-133 device copy size mismatch for %s.\n' "${candidate_id}" >&2
        exit 1
    fi
    remote_sha="$("${ADB[@]}" shell "toybox sha256sum '${REMOTE_ROOT}/model.gguf'" \
        | tr -d '\r' | awk '{print $1}')"
    if [[ "${remote_sha}" != "${expected_sha}" ]]; then
        printf 'RMA-133 device copy SHA-256 mismatch for %s: %s.\n' \
            "${candidate_id}" "${remote_sha}" >&2
        exit 1
    fi

    raw_path="${RESULTS_DIR}/${candidate_id}.raw.jsonl"
    printf 'RMA-133 benchmarking %s on %s.\n' "${candidate_id}" "${DEVICE_MODEL}"
    "${ADB[@]}" shell \
        "cd '${REMOTE_ROOT}' && LD_LIBRARY_PATH=. ./rma133_benchmark ./model.gguf '${candidate_id}' ./behavior_cases.tsv ./system_prompt.txt '${suffix}' '${context}' '${batch}' '${ubatch}' '${max_gen}' '${threads}' '${batch_threads}' '${temperature}' '${min_p}' '${seed}' '${queue}' '${thermal_abort}'" \
        | tr -d '\r' > "${raw_path}"
    "${ADB[@]}" shell rm -f -- "${REMOTE_ROOT}/model.gguf"

    report_path="${RESULTS_DIR}/${candidate_id}.report.json"
    python3 "${SCRIPT_DIR}/score_rma133_benchmark.py" score \
        --config "${CONFIG}" \
        --cases "${CASES}" \
        --raw "${raw_path}" \
        --candidate-id "${candidate_id}" \
        --output "${report_path}"
    report_args+=(--report "${report_path}")
done < "${RESULTS_DIR}/candidate_rows.tsv"

python3 "${SCRIPT_DIR}/score_rma133_benchmark.py" select \
    --config "${CONFIG}" \
    "${report_args[@]}" \
    --output "${RESULTS_DIR}/selection.json"

python3 - "${CONFIG}" "${RESULTS_DIR}/selection.json" "${RESULTS_DIR}/device.txt" \
    > "${RESULTS_DIR}/summary.txt" <<'PY'
import json
import sys
from pathlib import Path

config = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
selection = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
device = Path(sys.argv[3]).read_text(encoding="utf-8").strip()
print(f"benchmark_id={config['benchmark_id']}")
print(device)
print(f"status={selection['status']}")
print(f"selected_candidate_id={selection['selected_candidate_id']}")
for report in selection["candidate_reports"]:
    m = report["measurements"]
    print(
        "candidate=" + report["candidate_id"]
        + f" eligible={report['eligible']}"
        + f" quality={m['semantic_quality_score']:.2f}"
        + f" json={m['schema_reliability']:.3f}"
        + f" load_ms={m['load_time_ms']:.1f}"
        + f" decode_tps={m['mean_decode_tokens_per_second']:.2f}"
        + f" peak_rss={m['peak_rss_bytes']}"
        + f" battery_before={m['battery_temp_before_c']}"
        + f" battery_after={m['battery_temp_after_c']}"
    )
PY
cat "${RESULTS_DIR}/summary.txt"

#!/usr/bin/env python3
"""Re-run the selected RMA-133 V6 Qwen3 candidate under controlled device conditions."""

from __future__ import annotations

import importlib.util
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
ACCEPTED_V6_ROOT = Path(os.environ.get("RMA133_ACCEPTED_V6_ROOT", ROOT)).resolve()
BASE_PATH = ACCEPTED_V6_ROOT / "scripts/run_rma133_device_benchmark_v6.py"
BASE_SPEC = importlib.util.spec_from_file_location("run_rma133_device_benchmark_v6", BASE_PATH)
if BASE_SPEC is None or BASE_SPEC.loader is None:
    raise RuntimeError(f"Cannot load RMA-133 benchmark runner: {BASE_PATH}")
base = importlib.util.module_from_spec(BASE_SPEC)
sys.modules["run_rma133_device_benchmark_v6"] = base
BASE_SPEC.loader.exec_module(base)

SELECTED_CANDIDATE_ID = "qwen3-0.6b-q4-k-m"
ACCEPTED_V6_SOURCE_SHA = "e3007579d0365d31f5d5efc378fc81a13f2d705e"
FIRST_CASE_MAX_START_TEMP_C = float(
    os.environ.get("RMA133_REPRO_FIRST_CASE_MAX_START_TEMP_C", "32.5")
)
PRECONDITION = Path(
    os.environ.get(
        "RMA133_REPRO_PRECONDITION_OUTPUT",
        ROOT / "build/rma133/reproducibility/precondition.json",
    )
)
RESULTS = Path(
    os.environ.get(
        "RMA133_REPRO_RESULTS_DIR",
        ROOT / "build/rma133/reproducibility/results",
    )
)
REMOTE = (
    f"/data/local/tmp/reachy-rma133-v6-repro-{os.environ.get('GITHUB_RUN_ID', 'manual')}-"
    f"{os.getpid()}"
)


def require_precondition() -> dict[str, Any]:
    base.require_file(PRECONDITION)
    data = json.loads(PRECONDITION.read_text(encoding="utf-8"))
    if data.get("schema_version") != 1:
        raise RuntimeError("RMA-133 reproducibility precondition schema is unsupported")
    if data.get("protocol") != "rma133-v6-cool-start-reproducibility-v1":
        raise RuntimeError("RMA-133 reproducibility precondition protocol does not match")
    if data.get("status") != "passed":
        raise RuntimeError("RMA-133 reproducibility precondition did not pass")
    limits = data.get("limits")
    if (
        not isinstance(limits, dict)
        or float(limits.get("maximum_start_temperature_c", 0.0)) != 32.0
    ):
        raise RuntimeError("RMA-133 reproducibility precondition temperature contract changed")
    return data


def resolve_serial(precondition: dict[str, Any]) -> str:
    device = precondition.get("device")
    if not isinstance(device, dict) or not isinstance(device.get("serial"), str):
        raise RuntimeError("RMA-133 reproducibility precondition is missing device identity")
    serial = str(device["serial"])
    configured = os.environ.get("RMA133_DEVICE_SERIAL", "")
    if configured and configured != serial:
        raise RuntimeError("RMA-133 reproducibility device changed after preconditioning")
    if base.adb(serial, "get-state").stdout.strip() != "device":
        raise RuntimeError("RMA-133 reproducibility Android device is not ready")
    return serial


def selected_candidate(config: dict[str, Any]) -> dict[str, Any]:
    matches = [
        candidate
        for candidate in config.get("candidates", [])
        if candidate.get("candidate_id") == SELECTED_CANDIDATE_ID
    ]
    if len(matches) != 1:
        raise RuntimeError("frozen V6 config does not contain exactly one selected Qwen3 candidate")
    return matches[0]


def negative_control(
    serial: str,
    config: dict[str, Any],
    candidate: dict[str, Any],
) -> None:
    bad = RESULTS / "invalid-grammar.gbnf"
    bad.write_text("root ::= (\n", encoding="utf-8")
    base.adb(serial, "push", str(bad), f"{REMOTE}/invalid-grammar.gbnf")
    result = base.shell(
        serial,
        base.benchmark_command(
            config,
            candidate,
            f"{REMOTE}/invalid-grammar.gbnf",
            "negative-control/invalid-grammar.gbnf",
            base.sha256(bad),
        ),
        check=False,
    )
    raw = RESULTS / "constraint-negative-control.raw.jsonl"
    raw.write_text(result.stdout, encoding="utf-8")
    rows = [json.loads(line) for line in result.stdout.splitlines() if line.strip()]
    constraints = [row for row in rows if row.get("record") == "constraint"]
    cases = [row for row in rows if row.get("record") == "case"]
    if (
        result.returncode == 0
        or len(constraints) != 1
        or constraints[0].get("terminal_error_status") != 16
        or constraints[0].get("text_event_count") != 0
        or constraints[0].get("constrained_mode_active") is not False
        or len(cases) != 1
        or cases[0].get("response_bytes_hex") != ""
    ):
        raise RuntimeError("RMA-133 reproducibility malformed-grammar control did not fail closed")


def write_summary(
    precondition: dict[str, Any],
    report: dict[str, Any],
    status: str,
    reason: str | None,
) -> None:
    measurements = report["measurements"]
    summary = {
        "schema_version": 1,
        "protocol": "rma133-v6-cool-start-reproducibility-v1",
        "accepted_v6_source_sha": ACCEPTED_V6_SOURCE_SHA,
        "candidate_id": SELECTED_CANDIDATE_ID,
        "status": status,
        "reason": reason,
        "precondition": {
            "status": precondition["status"],
            "device": precondition["device"],
            "accepted_window": precondition.get("accepted_window"),
        },
        "first_case_maximum_start_temperature_c": FIRST_CASE_MAX_START_TEMP_C,
        "candidate_report": report,
        "observed": {
            "battery_temp_before_c": measurements["battery_temp_before_c"],
            "battery_peak_temp_c": measurements["battery_peak_temp_c"],
            "battery_temperature_rise_c": measurements["battery_temperature_rise_c"],
            "mean_decode_tokens_per_second": measurements["mean_decode_tokens_per_second"],
            "schema_reliability": measurements["schema_reliability"],
            "semantic_quality_score": measurements["semantic_quality_score"],
            "peak_rss_bytes": measurements["peak_rss_bytes"],
        },
    }
    (RESULTS / "reproducibility.json").write_text(
        json.dumps(summary, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    lines = [
        f"status={status}",
        f"candidate_id={SELECTED_CANDIDATE_ID}",
        f"eligible={report['eligible']}",
        f"first_case_battery_c={measurements['battery_temp_before_c']:.1f}",
        f"peak_battery_c={measurements['battery_peak_temp_c']:.1f}",
        f"decode_tps={measurements['mean_decode_tokens_per_second']:.4f}",
        f"schema={measurements['schema_reliability']:.3f}",
        f"semantic={measurements['semantic_quality_score']:.4f}",
        f"peak_rss={measurements['peak_rss_bytes']}",
    ]
    if reason:
        lines.append(f"reason={reason}")
    (RESULTS / "summary.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))


def main() -> int:
    for tool in ("adb", "curl"):
        if shutil.which(tool) is None:
            raise SystemExit(f"RMA-133 reproducibility required command missing: {tool}")
    for path in (
        base.CONFIG,
        base.CASES,
        base.PROMPT,
        base.GRAMMAR,
        base.RUNTIME,
        base.BENCH,
        base.SCORER,
        PRECONDITION,
    ):
        base.require_file(path)

    subprocess.run(
        [
            sys.executable,
            str(base.SCORER),
            "validate",
            "--config",
            str(base.CONFIG),
            "--cases",
            str(base.CASES),
        ],
        check=True,
    )
    config = json.loads(base.CONFIG.read_text(encoding="utf-8"))
    contract = config["constrained_generation_contract"]
    candidate = selected_candidate(config)
    precondition = require_precondition()
    serial = resolve_serial(precondition)

    RESULTS.mkdir(parents=True, exist_ok=True)
    for old in RESULTS.iterdir():
        if old.is_file() or old.is_symlink():
            old.unlink()
        elif old.is_dir():
            shutil.rmtree(old)

    base.REMOTE = REMOTE
    base.shell(serial, ["rm", "-rf", REMOTE], check=False)
    base.shell(serial, ["mkdir", "-p", REMOTE])
    try:
        for local, remote in (
            (base.RUNTIME, "libreachy_llama.so"),
            (base.BENCH, "rma133_benchmark_v6"),
            (base.CASES, "behavior_cases-v2.tsv"),
            (base.PROMPT, "system_prompt-v4.txt"),
            (base.GRAMMAR, "behavior-output-v1.gbnf"),
        ):
            base.adb(serial, "push", str(local), f"{REMOTE}/{remote}")
        base.shell(serial, ["chmod", "0755", f"{REMOTE}/rma133_benchmark_v6"])
        remote_grammar_sha = base.shell(
            serial,
            ["toybox", "sha256sum", f"{REMOTE}/behavior-output-v1.gbnf"],
        ).stdout.split()[0]
        if remote_grammar_sha != contract["grammar_sha256"]:
            raise RuntimeError("RMA-133 reproducibility remote grammar SHA-256 mismatch")

        artifact = candidate["artifact"]
        cached = base.model_cache(candidate)
        free_kib = int(
            base.shell(serial, ["df", "-Pk", "/data/local/tmp"]).stdout.splitlines()[-1].split()[3]
        )
        need_kib = (artifact["file_size_bytes"] + 1023) // 1024 + 262144
        if free_kib < need_kib:
            raise RuntimeError("RMA-133 reproducibility device storage is insufficient")
        base.shell(serial, ["rm", "-f", f"{REMOTE}/model.gguf"], check=False)
        base.adb(serial, "push", str(cached), f"{REMOTE}/model.gguf")
        remote_sha = base.shell(
            serial,
            ["toybox", "sha256sum", f"{REMOTE}/model.gguf"],
        ).stdout.split()[0]
        remote_size = int(
            base.shell(
                serial,
                ["stat", "-c", "%s", f"{REMOTE}/model.gguf"],
            ).stdout.strip()
        )
        if remote_sha != artifact["sha256"] or remote_size != artifact["file_size_bytes"]:
            raise RuntimeError("RMA-133 reproducibility device model integrity failure")

        negative_control(serial, config, candidate)

        process = base.shell(
            serial,
            base.benchmark_command(
                config,
                candidate,
                f"{REMOTE}/behavior-output-v1.gbnf",
                contract["grammar_path"],
                contract["grammar_sha256"],
            ),
            check=False,
        )
        raw = RESULTS / f"{SELECTED_CANDIDATE_ID}.raw.jsonl"
        raw.write_text(process.stdout, encoding="utf-8")
        (RESULTS / f"{SELECTED_CANDIDATE_ID}.benchmark-exit-code.txt").write_text(
            f"{process.returncode}\n",
            encoding="utf-8",
        )
        report_path = RESULTS / f"{SELECTED_CANDIDATE_ID}.report.json"
        subprocess.run(
            [
                sys.executable,
                str(base.SCORER),
                "score",
                "--config",
                str(base.CONFIG),
                "--cases",
                str(base.CASES),
                "--raw",
                str(raw),
                "--candidate-id",
                SELECTED_CANDIDATE_ID,
                "--output",
                str(report_path),
            ],
            check=True,
        )
        report = json.loads(report_path.read_text(encoding="utf-8"))
        first_case_temp = float(report["measurements"]["battery_temp_before_c"])
        if first_case_temp > FIRST_CASE_MAX_START_TEMP_C:
            reason = (
                f"first benchmark battery reading {first_case_temp:.1f} C exceeded "
                f"reproducibility validity ceiling {FIRST_CASE_MAX_START_TEMP_C:.1f} C"
            )
            write_summary(precondition, report, "invalid_environment", reason)
            return 2
        if not report.get("eligible", False):
            reasons = report.get("rejection_reasons", [])
            reason = "; ".join(str(item) for item in reasons) or "frozen V6 candidate gates failed"
            write_summary(precondition, report, "candidate_gate_failure", reason)
            return 1

        write_summary(precondition, report, "reproducible_pass", None)
        return 0
    finally:
        base.shell(serial, ["rm", "-rf", REMOTE], check=False)


if __name__ == "__main__":
    raise SystemExit(main())

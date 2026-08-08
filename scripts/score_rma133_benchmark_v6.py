#!/usr/bin/env python3
"""Fail-closed RMA-133 V6 scorer for constrained local-model generation."""
from __future__ import annotations

import argparse
import copy
import hashlib
import importlib.util
import json
import math
import statistics
from dataclasses import dataclass
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
_spec = importlib.util.spec_from_file_location("rma133_legacy", HERE / "score_rma133_benchmark.py")
if _spec is None or _spec.loader is None:
    raise RuntimeError("historical RMA-133 scorer is unavailable")
legacy = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(legacy)

BENCHMARK_ID = "rma133-initial-local-model-v3"
CONSTRAINT = {
    "constraint_type": "GBNF",
    "grammar_path": "benchmarks/rma133/behavior-output-v1.gbnf",
    "grammar_sha256": "2c333f6bb576e025c80b0e4050bbc816247817ebe6f145361360e6eec71eb734",
    "grammar_root": "root",
    "behavior_cases_path": "benchmarks/rma133/behavior_cases-v2.tsv",
    "behavior_cases_sha256": "f5df82ec92022192a351a0bb61d7c2ef2e8b71206de4a941a10e547735f18cfa",
}

@dataclass(frozen=True)
class Case:
    case_id: str
    gaze_kind: str | None
    gaze_entity: str | None
    expression: str
    gesture: str
    urgency: str
    speech_groups: tuple[tuple[str, ...], ...]
    forbidden: tuple[str, ...]


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"expected JSON object: {path}")
    return value


def _hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_config(path: Path, cases_path: Path | None = None) -> dict[str, Any]:
    config = load_json(path)
    if config.get("benchmark_id") != BENCHMARK_ID:
        raise ValueError("V6 benchmark_id must use the v3 constrained-generation lineage")
    compat = copy.deepcopy(config)
    compat["benchmark_id"] = "rma133-initial-local-model-v2"
    compat.pop("constrained_generation_contract", None)
    legacy._validate_config(compat)
    v5 = load_json(path.parent / "candidates-v5.json")
    for key in ("runtime_profile", "selection_policy", "candidates", "license_policy", "model_size_policy", "system_prompt_contract"):
        if config.get(key) != v5.get(key):
            raise ValueError(f"V6 changed frozen V5 field: {key}")
    contract = config.get("constrained_generation_contract")
    if contract != CONSTRAINT:
        raise ValueError("V6 constrained-generation contract changed after freeze")
    for file_key, hash_key in (("grammar_path", "grammar_sha256"), ("behavior_cases_path", "behavior_cases_sha256")):
        target = ROOT / contract[file_key]
        if _hash(target) != contract[hash_key]:
            raise ValueError(f"V6 frozen hash mismatch: {target}")
    prompt = config["system_prompt_contract"]
    if _hash(ROOT / prompt["path"]) != prompt["sha256"]:
        raise ValueError("V6 system prompt hash mismatch")
    if cases_path is not None and cases_path.resolve() != (ROOT / contract["behavior_cases_path"]).resolve():
        raise ValueError("V6 scorer was given a cases file outside the frozen contract")
    return config


def _groups(raw: str, line: int) -> tuple[tuple[str, ...], ...]:
    if not raw or raw == "-":
        raise ValueError(f"line {line}: speech_required_groups must be non-empty")
    groups = tuple(tuple(term.strip().casefold() for term in group.split("|") if term.strip()) for group in raw.split(";"))
    if any(not group for group in groups):
        raise ValueError(f"line {line}: empty semantic speech group")
    return groups


def load_cases(path: Path) -> list[Case]:
    lines = path.read_text(encoding="utf-8").splitlines()
    header = ["case_id", "prompt", "expected_gaze_kind", "expected_gaze_entity", "expected_expression", "expected_gesture", "expected_urgency", "speech_required_groups", "speech_forbidden_terms"]
    if not lines or lines[0].split("\t") != header:
        raise ValueError("V6 behavior-case header mismatch")
    result: list[Case] = []
    seen: set[str] = set()
    for line_no, line in enumerate(lines[1:], 2):
        if not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) != len(header):
            raise ValueError(f"line {line_no}: expected {len(header)} fields")
        case_id, _prompt, gaze_kind, gaze_entity, expression, gesture, urgency, required, forbidden = parts
        if not case_id or case_id in seen:
            raise ValueError(f"line {line_no}: duplicate/blank case id")
        seen.add(case_id)
        forbidden_terms = () if forbidden == "-" else tuple(t.strip().casefold() for t in forbidden.split("|") if t.strip())
        result.append(Case(case_id, None if gaze_kind == "-" else gaze_kind, None if gaze_entity == "-" else gaze_entity, expression, gesture, urgency, _groups(required, line_no), forbidden_terms))
    if len(result) != 12:
        raise ValueError("V6 requires exactly 12 frozen behavior cases")
    return result


def score_case(case: Case, record: dict[str, Any]) -> dict[str, Any]:
    text, text_reasons = legacy._response_text_from_record(record)
    if text is None:
        return {"case_id": case.case_id, "schema_valid": False, "semantic_score": 0.0, "reasons": text_reasons}
    value, reasons = legacy._validate_behavior_object(text)
    if value is None or reasons:
        return {"case_id": case.case_id, "schema_valid": False, "semantic_score": 0.0, "reasons": reasons}
    speech = str(value["speech"]).casefold()
    semantic: list[str] = []
    score = 10.0
    if all(any(term in speech for term in group) for group in case.speech_groups) and not any(term in speech for term in case.forbidden):
        score += 25.0
    else:
        semantic.append("speech did not satisfy required concepts or contained a forbidden concept")
    gaze = value.get("gaze_target")
    if case.gaze_kind is None:
        if gaze is None:
            score += 25.0
        else:
            semantic.append("unexpected gaze target")
    elif isinstance(gaze, dict) and gaze.get("kind") == case.gaze_kind and gaze.get("entity_id") == case.gaze_entity:
        score += 25.0
    else:
        semantic.append("gaze target mismatch")
    for key, expected, points in (("expression", case.expression, 15.0), ("gesture", case.gesture, 15.0), ("urgency", case.urgency, 10.0)):
        if value.get(key) == expected:
            score += points
        else:
            semantic.append(f"{key} mismatch")
    return {"case_id": case.case_id, "schema_valid": True, "semantic_score": score, "reasons": semantic}


def _finite(value: Any) -> bool:
    return legacy._is_finite_number(value)


def score_candidate(config_path: Path, cases_path: Path, raw_path: Path, candidate_id: str, output_path: Path) -> dict[str, Any]:
    config = validate_config(config_path, cases_path)
    cases = load_cases(cases_path)
    candidate = legacy._candidate_config(config, candidate_id)
    records = legacy._load_jsonl(raw_path)
    if any(r.get("candidate_id") != candidate_id for r in records):
        raise ValueError("benchmark evidence contains another candidate id")
    models = [r for r in records if r.get("record") == "model"]
    summaries = [r for r in records if r.get("record") == "summary"]
    constraints = [r for r in records if r.get("record") == "constraint"]
    case_records = [r for r in records if r.get("record") == "case"]
    if len(models) != 1 or len(summaries) != 1 or len(constraints) != 1:
        raise ValueError("V6 evidence requires exactly one model, constraint, and summary record")
    by_case: dict[str, dict[str, Any]] = {}
    expected_ids = {c.case_id for c in cases}
    for record in case_records:
        case_id = record.get("case_id")
        if not isinstance(case_id, str) or case_id in by_case or case_id not in expected_ids:
            raise ValueError("V6 evidence contains duplicate/unknown case id")
        by_case[case_id] = record
    case_scores = [score_case(c, by_case[c.case_id]) if c.case_id in by_case else {"case_id": c.case_id, "schema_valid": False, "semantic_score": 0.0, "reasons": ["required case missing"]} for c in cases]
    policy = config["selection_policy"]
    constraint = constraints[0]
    expected_constraint = {
        "candidate_id": candidate_id, "runtime_abi_version": 2, "constraint_type": "GBNF",
        "grammar_path": CONSTRAINT["grammar_path"], "grammar_sha256": CONSTRAINT["grammar_sha256"],
        "grammar_root": "root", "constrained_mode_active": True,
        "constrained_start_attempts": 12, "constrained_start_successes": 12,
        "terminal_error_status": 0, "base_exit_code": 0,
    }
    constraint_reasons = [f"constraint evidence mismatch for {k}" for k, v in expected_constraint.items() if constraint.get(k) != v]
    completed = sum(r.get("completed") is True for r in case_records)
    schema = sum(s["schema_valid"] for s in case_scores) / len(case_scores)
    quality = statistics.fmean(float(s["semantic_score"]) for s in case_scores)
    rates = [float(r["decode_tokens_per_second"]) for r in case_records if _finite(r.get("decode_tokens_per_second")) and float(r["decode_tokens_per_second"]) > 0]
    decode = statistics.fmean(rates) if rates else 0.0
    peak = max((int(r["peak_rss_bytes"]) for r in records if isinstance(r.get("peak_rss_bytes"), int) and r["peak_rss_bytes"] >= 0), default=0)
    model, summary = models[0], summaries[0]
    before, after = model.get("battery_temp_before_c"), summary.get("battery_temp_after_c")
    temps = [float(r[k]) for r in records for k in ("battery_temp_before_c", "battery_temp_c", "battery_temp_after_c") if _finite(r.get(k)) and float(r[k]) > 0]
    battery_ok = _finite(before) and float(before) > 0 and _finite(after) and float(after) > 0 and bool(temps)
    battery_peak = max(temps) if battery_ok else math.inf
    battery_rise = battery_peak - float(before) if battery_ok else math.inf
    ttft = [float(r["time_to_first_text_ms"]) for r in case_records if _finite(r.get("time_to_first_text_ms")) and float(r["time_to_first_text_ms"]) >= 0]
    reject = list(constraint_reasons)
    checks = [
        (completed != policy["required_completed_cases"], f"completed {completed}/12 required cases"),
        (schema < policy["minimum_schema_reliability"], f"schema reliability {schema:.3f} below gate"),
        (quality < policy["minimum_semantic_quality_score"], f"semantic quality {quality:.2f} below gate"),
        (decode < policy["minimum_mean_decode_tokens_per_second"], f"decode {decode:.2f} tok/s below gate"),
        (peak <= 0 or peak > policy["maximum_peak_rss_bytes"], f"peak RSS {peak} outside gate"),
        (not battery_ok, "battery temperature unavailable"),
        (battery_ok and battery_peak >= policy["maximum_battery_temperature_c"], f"battery peak {battery_peak:.1f} C reached gate"),
        (battery_ok and battery_rise > policy["maximum_battery_temperature_rise_c"], f"battery rise {battery_rise:.1f} C above gate"),
    ]
    reject.extend(message for failed, message in checks if failed)
    report = {
        "schema_version": 1, "benchmark_id": BENCHMARK_ID, "candidate_id": candidate_id,
        "source_revision": candidate["source_revision"], "artifact_sha256": candidate["artifact"]["sha256"],
        "eligible": not reject, "rejection_reasons": reject, "constraint_evidence_valid": not constraint_reasons,
        "measurements": {
            "load_time_ms": float(model.get("load_time_ms", math.inf)), "parameter_count": model.get("parameter_count"),
            "tensor_bytes": model.get("tensor_bytes"), "training_context_tokens": model.get("training_context_tokens"),
            "peak_rss_bytes": peak, "schema_reliability": schema, "semantic_quality_score": quality,
            "mean_decode_tokens_per_second": decode, "mean_time_to_first_text_ms": statistics.fmean(ttft) if ttft else None,
            "battery_temp_before_c": before, "battery_temp_after_c": after,
            "battery_peak_temp_c": battery_peak if battery_ok else None,
            "battery_temperature_rise_c": battery_rise if battery_ok else None,
            "thermal_zone_max_before_c": model.get("thermal_zone_max_before_c"),
            "thermal_zone_max_after_c": summary.get("thermal_zone_max_after_c"), "completed_cases": completed,
        }, "case_scores": case_scores,
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return report


def select_candidate(config_path: Path, report_paths: list[Path], output_path: Path) -> dict[str, Any]:
    config = validate_config(config_path)
    reports = [load_json(path) for path in report_paths]
    expected = {c["candidate_id"] for c in config["candidates"]}
    if {r.get("candidate_id") for r in reports} != expected or len(reports) != len(expected):
        raise ValueError("selector requires exactly one report for every frozen V6 candidate")
    eligible = [r for r in reports if r.get("eligible") is True]
    eligible.sort(key=lambda r: (-float(r["measurements"]["semantic_quality_score"]), -float(r["measurements"]["schema_reliability"]), -float(r["measurements"]["mean_decode_tokens_per_second"]), int(r["measurements"]["peak_rss_bytes"]), float(r["measurements"]["load_time_ms"]), str(r["candidate_id"])))
    selected = eligible[0]["candidate_id"] if eligible else None
    result = {"schema_version": 1, "benchmark_id": BENCHMARK_ID, "status": "selected" if selected else "no_candidate_passed", "selected_candidate_id": selected, "candidate_reports": reports}
    output_path.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({"status": result["status"], "selected_candidate_id": selected}, sort_keys=True))
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    validate = sub.add_parser("validate"); validate.add_argument("--config", type=Path, required=True); validate.add_argument("--cases", type=Path)
    score = sub.add_parser("score"); score.add_argument("--config", type=Path, required=True); score.add_argument("--cases", type=Path, required=True); score.add_argument("--raw", type=Path, required=True); score.add_argument("--candidate-id", required=True); score.add_argument("--output", type=Path, required=True)
    select = sub.add_parser("select"); select.add_argument("--config", type=Path, required=True); select.add_argument("--report", type=Path, action="append", required=True); select.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.command == "validate":
            validate_config(args.config, args.cases); load_cases(args.cases or ROOT / CONSTRAINT["behavior_cases_path"]); return 0
        if args.command == "score":
            report = score_candidate(args.config, args.cases, args.raw, args.candidate_id, args.output); print(json.dumps({"candidate_id": report["candidate_id"], "eligible": report["eligible"]}, sort_keys=True)); return 0
        result = select_candidate(args.config, args.report, args.output); return 0 if result["selected_candidate_id"] else 1
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        parser.error(str(exc))
    return 2

if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Score and select RMA-133 local-model benchmark candidates from device evidence."""

from __future__ import annotations

import argparse
import json
import math
import re
import statistics
from dataclasses import dataclass
from pathlib import Path
from typing import Any

ALLOWED_TOP_LEVEL_KEYS = {
    "schema_version",
    "speech",
    "gaze_target",
    "expression",
    "gesture",
    "urgency",
}
ALLOWED_EXPRESSIONS = {
    "neutral",
    "attentive",
    "curious",
    "pleased",
    "concerned",
    "surprised",
}
ALLOWED_GESTURES = {"none", "nod", "small_head_tilt", "recoil"}
ALLOWED_URGENCY = {"low", "normal", "high"}
EXPECTED_RANKING = [
    "semantic_quality_score_desc",
    "schema_reliability_desc",
    "mean_decode_tokens_per_second_desc",
    "peak_rss_bytes_asc",
    "load_time_ms_asc",
    "candidate_id_asc",
]
UNSAFE_KEY_FRAGMENTS = {
    "joint",
    "torque",
    "motor",
    "velocity",
    "angle",
    "position",
    "coordinate",
    "command",
}


@dataclass(frozen=True)
class CaseExpectation:
    case_id: str
    prompt: str
    gaze_kind: str | None
    gaze_entity: str | None
    expression: str
    gesture: str
    urgency: str
    speech_any_terms: tuple[str, ...]


def _load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"expected JSON object in {path}")
    return value


def _validate_config(config: dict[str, Any]) -> None:
    if config.get("schema_version") != 1:
        raise ValueError("RMA-133 candidate config schema_version must be 1")
    benchmark_id = config.get("benchmark_id")
    allowed_benchmark_ids = {
        "rma133-initial-sub1b-v1",
        "rma133-initial-local-model-v2",
    }
    if benchmark_id not in allowed_benchmark_ids:
        raise ValueError("RMA-133 benchmark_id is not an accepted frozen contract lineage")
    relaxed_size_contract = benchmark_id == "rma133-initial-local-model-v2"

    license_policy = config.get("license_policy")
    if not isinstance(license_policy, dict):
        raise ValueError("RMA-133 license policy is missing")
    allowed = license_policy.get("allowed_spdx_ids")
    if allowed != ["Apache-2.0"]:
        raise ValueError("RMA-133 v1 allows exactly Apache-2.0 candidates")

    profile = config.get("runtime_profile")
    expected_profile = {
        "context_tokens": 2048,
        "batch_tokens": 256,
        "micro_batch_tokens": 64,
        "max_generated_tokens": 128,
        "threads": 4,
        "batch_threads": 4,
        "temperature": 0.0,
        "min_p": 0.0,
        "seed": 133,
        "stream_queue_capacity": 64,
    }
    if profile != expected_profile:
        raise ValueError("RMA-133 v1 runtime profile changed after the benchmark was frozen")

    policy = config.get("selection_policy")
    if not isinstance(policy, dict) or policy.get("ranking") != EXPECTED_RANKING:
        raise ValueError("RMA-133 v1 ranking policy is missing or changed")
    expected_thresholds = {
        "required_completed_cases": 12,
        "minimum_schema_reliability": 1.0,
        "minimum_semantic_quality_score": 85.0,
        "minimum_mean_decode_tokens_per_second": 1.0,
        "maximum_peak_rss_bytes": 1_500_000_000,
        "maximum_battery_temperature_c": 45.0,
        "maximum_battery_temperature_rise_c": 10.0,
    }
    for key, expected in expected_thresholds.items():
        if policy.get(key) != expected:
            raise ValueError(f"RMA-133 v1 selection threshold changed: {key}")

    candidates = config.get("candidates")
    if not isinstance(candidates, list) or len(candidates) < 2:
        raise ValueError("RMA-133 requires Qwen3-0.6B-class and at least one alternative")
    ids: set[str] = set()
    has_qwen = False
    has_sub1b_alternative = False
    has_relaxed_alternative = False
    quantizations: set[str] = set()
    for candidate in candidates:
        if not isinstance(candidate, dict):
            raise ValueError("RMA-133 candidate entry is not an object")
        candidate_id = candidate.get("candidate_id")
        if not isinstance(candidate_id, str) or not candidate_id or candidate_id in ids:
            raise ValueError("RMA-133 candidate IDs must be unique nonblank strings")
        if re.fullmatch(r"[a-z0-9][a-z0-9.-]{0,127}", candidate_id) is None:
            raise ValueError(f"candidate {candidate_id!r} has an unsafe candidate ID")
        ids.add(candidate_id)
        model_class = candidate.get("model_class")
        allowed_model_classes = {
            "qwen3-0.6b-class",
            "alternative-sub1b",
            "alternative-local",
        }
        if model_class not in allowed_model_classes:
            raise ValueError(f"candidate {candidate_id} has an unknown model class")
        if relaxed_size_contract and model_class == "alternative-sub1b":
            raise ValueError("relaxed-size contract cannot label an alternative as sub-1B")
        if not relaxed_size_contract and model_class == "alternative-local":
            raise ValueError("sub-1B contract cannot include a relaxed-size alternative")
        has_qwen = has_qwen or model_class == "qwen3-0.6b-class"
        has_sub1b_alternative = has_sub1b_alternative or model_class == "alternative-sub1b"
        has_relaxed_alternative = has_relaxed_alternative or model_class == "alternative-local"
        license_id = candidate.get("license_id")
        if license_id not in allowed:
            raise ValueError(f"candidate {candidate_id} is outside the frozen license policy")
        revision = candidate.get("source_revision")
        if (
            not isinstance(revision, str)
            or len(revision) != 40
            or any(char not in "0123456789abcdef" for char in revision)
        ):
            raise ValueError(f"candidate {candidate_id} does not use an immutable Git revision")
        artifact = candidate.get("artifact")
        if not isinstance(artifact, dict):
            raise ValueError(f"candidate {candidate_id} has no artifact record")
        filename = artifact.get("filename")
        if (
            not isinstance(filename, str)
            or not filename.endswith(".gguf")
            or len(filename) > 200
            or "/" in filename
            or "\\" in filename
            or filename in {".", ".."}
        ):
            raise ValueError(f"candidate {candidate_id} artifact filename is unsafe")
        url = artifact.get("url")
        sha256 = artifact.get("sha256")
        size = artifact.get("file_size_bytes")
        quantization = artifact.get("quantization")
        if not isinstance(url, str) or not url.startswith("https://") or revision not in url:
            raise ValueError(f"candidate {candidate_id} artifact URL is not revision-pinned HTTPS")
        if (
            not isinstance(sha256, str)
            or len(sha256) != 64
            or any(char not in "0123456789abcdef" for char in sha256)
        ):
            raise ValueError(f"candidate {candidate_id} artifact SHA-256 is invalid")
        if not isinstance(size, int) or isinstance(size, bool) or size <= 0:
            raise ValueError(f"candidate {candidate_id} artifact size is invalid")
        if not isinstance(quantization, str) or not quantization:
            raise ValueError(f"candidate {candidate_id} quantization is missing")
        quantizations.add(quantization)
        suffix = candidate.get("user_prompt_suffix")
        expected_suffix = "/no_think" if model_class == "qwen3-0.6b-class" else ""
        if suffix != expected_suffix:
            raise ValueError(
                f"candidate {candidate_id} prompt suffix changed from the frozen v1 contract"
            )
    if not has_qwen:
        raise ValueError("RMA-133 candidate set must retain the Qwen3-0.6B control")
    if relaxed_size_contract:
        if not has_relaxed_alternative:
            raise ValueError("RMA-133 relaxed-size contract requires a larger local alternative")
    elif not has_sub1b_alternative:
        raise ValueError("RMA-133 sub-1B contract requires a sub-1B alternative")
    if len(quantizations) != 1:
        raise ValueError("RMA-133 v1 candidates must use the same quantization class")


def _load_cases(path: Path) -> list[CaseExpectation]:
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines:
        raise ValueError("behavior case file is empty")
    header = lines[0].split("\t")
    expected_header = [
        "case_id",
        "prompt",
        "expected_gaze_kind",
        "expected_gaze_entity",
        "expected_expression",
        "expected_gesture",
        "expected_urgency",
        "speech_any_terms",
    ]
    if header != expected_header:
        raise ValueError("behavior case header does not match the RMA-133 contract")

    cases: list[CaseExpectation] = []
    seen: set[str] = set()
    for line_number, line in enumerate(lines[1:], start=2):
        if not line.strip():
            continue
        fields = line.split("\t")
        if len(fields) != len(expected_header):
            raise ValueError(f"line {line_number}: expected {len(expected_header)} fields")
        case_id, prompt, gaze_kind, gaze_entity, expression, gesture, urgency, terms = fields
        if case_id in seen:
            raise ValueError(f"duplicate case id: {case_id}")
        seen.add(case_id)
        cases.append(
            CaseExpectation(
                case_id=case_id,
                prompt=prompt,
                gaze_kind=None if gaze_kind == "-" else gaze_kind,
                gaze_entity=None if gaze_entity == "-" else gaze_entity,
                expression=expression,
                gesture=gesture,
                urgency=urgency,
                speech_any_terms=tuple(term.casefold() for term in terms.split("|") if term),
            )
        )
    return cases


def _load_jsonl(path: Path) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip():
            continue
        value = json.loads(line)
        if not isinstance(value, dict):
            raise ValueError(f"{path}:{line_number}: record is not a JSON object")
        records.append(value)
    return records


def _is_finite_number(value: Any) -> bool:
    return isinstance(value, int | float) and not isinstance(value, bool) and math.isfinite(value)


def _unsafe_key_present(value: Any) -> bool:
    if isinstance(value, dict):
        for key, child in value.items():
            lowered = str(key).casefold()
            if any(fragment in lowered for fragment in UNSAFE_KEY_FRAGMENTS):
                return True
            if _unsafe_key_present(child):
                return True
    elif isinstance(value, list):
        return any(_unsafe_key_present(child) for child in value)
    return False


def _validate_behavior_object(response: str) -> tuple[dict[str, Any] | None, list[str]]:
    reasons: list[str] = []
    stripped = response.strip()
    if not stripped.startswith("{") or not stripped.endswith("}"):
        return None, ["response is not exactly one JSON object"]
    try:
        value = json.loads(stripped)
    except json.JSONDecodeError as exc:
        return None, [f"invalid JSON: {exc.msg}"]
    if not isinstance(value, dict):
        return None, ["top-level response is not an object"]

    unknown = sorted(set(value) - ALLOWED_TOP_LEVEL_KEYS)
    if unknown:
        reasons.append(f"unknown top-level keys: {', '.join(unknown)}")
    if _unsafe_key_present(value):
        reasons.append("unsafe raw-actuation key present")
    if value.get("schema_version") != 1:
        reasons.append("schema_version is not 1")

    speech = value.get("speech")
    if not isinstance(speech, str) or not speech.strip() or len(speech) > 160:
        reasons.append("speech is missing, blank, or longer than 160 characters")
    expression = value.get("expression")
    if expression not in ALLOWED_EXPRESSIONS:
        reasons.append("expression is outside the benchmark vocabulary")
    gesture = value.get("gesture")
    if gesture not in ALLOWED_GESTURES:
        reasons.append("gesture is outside the benchmark vocabulary")
    urgency = value.get("urgency")
    if urgency not in ALLOWED_URGENCY:
        reasons.append("urgency is outside the benchmark vocabulary")

    gaze = value.get("gaze_target")
    if gaze is not None:
        if not isinstance(gaze, dict):
            reasons.append("gaze_target is not an object")
        else:
            if set(gaze) != {"kind", "entity_id"}:
                reasons.append("gaze_target does not contain exactly kind and entity_id")
            if gaze.get("kind") != "tracked_entity":
                reasons.append("gaze_target kind is not tracked_entity")
            entity_id = gaze.get("entity_id")
            if not isinstance(entity_id, str) or not entity_id.startswith("entity-"):
                reasons.append("gaze_target entity_id is invalid")
    return value, reasons


def _response_text_from_record(record: dict[str, Any]) -> tuple[str | None, list[str]]:
    response_hex = record.get("response_bytes_hex")
    if response_hex is not None:
        if not isinstance(response_hex, str) or len(response_hex) % 2 != 0:
            return None, ["benchmark response byte encoding is invalid"]
        try:
            response_bytes = bytes.fromhex(response_hex)
        except ValueError:
            return None, ["benchmark response byte encoding is invalid"]
        try:
            return response_bytes.decode("utf-8"), []
        except UnicodeDecodeError:
            return None, ["response is not valid UTF-8"]

    response = record.get("response")
    if not isinstance(response, str):
        return None, ["benchmark record has no response bytes"]
    return response, []


def _score_case(expectation: CaseExpectation, record: dict[str, Any]) -> dict[str, Any]:
    response, response_reasons = _response_text_from_record(record)
    if response is None:
        return {
            "case_id": expectation.case_id,
            "schema_valid": False,
            "semantic_score": 0.0,
            "reasons": response_reasons,
        }

    value, reasons = _validate_behavior_object(response)
    if value is None or reasons:
        return {
            "case_id": expectation.case_id,
            "schema_valid": False,
            "semantic_score": 0.0,
            "reasons": reasons,
        }

    score = 10.0
    semantic_reasons: list[str] = []
    speech = str(value["speech"]).casefold()
    if expectation.speech_any_terms and any(
        term in speech for term in expectation.speech_any_terms
    ):
        score += 25.0
    else:
        semantic_reasons.append("speech did not contain an expected semantic term")

    gaze = value.get("gaze_target")
    if expectation.gaze_kind is None:
        if gaze is None:
            score += 25.0
        else:
            semantic_reasons.append("unexpected gaze target")
    elif (
        isinstance(gaze, dict)
        and gaze.get("kind") == expectation.gaze_kind
        and gaze.get("entity_id") == expectation.gaze_entity
    ):
        score += 25.0
    else:
        semantic_reasons.append("gaze target did not match the expected tracked entity")

    if value.get("expression") == expectation.expression:
        score += 15.0
    else:
        semantic_reasons.append("expression mismatch")
    if value.get("gesture") == expectation.gesture:
        score += 15.0
    else:
        semantic_reasons.append("gesture mismatch")
    if value.get("urgency") == expectation.urgency:
        score += 10.0
    else:
        semantic_reasons.append("urgency mismatch")

    return {
        "case_id": expectation.case_id,
        "schema_valid": True,
        "semantic_score": score,
        "reasons": semantic_reasons,
    }


def _candidate_config(config: dict[str, Any], candidate_id: str) -> dict[str, Any]:
    matches = [
        candidate for candidate in config["candidates"] if candidate["candidate_id"] == candidate_id
    ]
    if len(matches) != 1:
        raise ValueError(f"candidate {candidate_id!r} is missing or duplicated")
    return matches[0]


def score_candidate(
    *,
    config_path: Path,
    cases_path: Path,
    raw_path: Path,
    candidate_id: str,
    output_path: Path,
) -> dict[str, Any]:
    config = _load_json(config_path)
    _validate_config(config)
    candidate = _candidate_config(config, candidate_id)
    expectations = _load_cases(cases_path)
    records = _load_jsonl(raw_path)

    model_records = [record for record in records if record.get("record") == "model"]
    summary_records = [record for record in records if record.get("record") == "summary"]
    case_records = [record for record in records if record.get("record") == "case"]
    if len(model_records) != 1 or len(summary_records) != 1:
        raise ValueError("benchmark evidence must contain exactly one model and one summary record")
    if any(record.get("candidate_id") != candidate_id for record in records):
        raise ValueError("benchmark evidence contains a different candidate id")

    by_case: dict[str, dict[str, Any]] = {}
    for record in case_records:
        case_id = record.get("case_id")
        if not isinstance(case_id, str) or case_id in by_case:
            raise ValueError("benchmark evidence contains a missing or duplicate case id")
        by_case[case_id] = record
    if set(by_case) != {case.case_id for case in expectations}:
        raise ValueError("benchmark evidence case set does not match the frozen corpus")

    case_scores = [_score_case(case, by_case[case.case_id]) for case in expectations]
    schema_reliability = sum(1 for score in case_scores if score["schema_valid"]) / len(case_scores)
    semantic_quality = statistics.fmean(score["semantic_score"] for score in case_scores)
    completed_cases = sum(1 for record in case_records if record.get("completed") is True)

    decode_rates = [
        float(record["decode_tokens_per_second"])
        for record in case_records
        if _is_finite_number(record.get("decode_tokens_per_second"))
        and float(record["decode_tokens_per_second"]) > 0.0
    ]
    mean_decode_rate = statistics.fmean(decode_rates) if decode_rates else 0.0

    model_record = model_records[0]
    summary = summary_records[0]
    peak_values = [
        int(record["peak_rss_bytes"])
        for record in records
        if isinstance(record.get("peak_rss_bytes"), int) and record["peak_rss_bytes"] >= 0
    ]
    peak_rss = max(peak_values, default=0)
    load_time_ms = float(model_record.get("load_time_ms", math.inf))
    battery_before = model_record.get("battery_temp_before_c")
    battery_after = summary.get("battery_temp_after_c")
    battery_samples = []
    for record in records:
        for key in ("battery_temp_before_c", "battery_temp_c", "battery_temp_after_c"):
            value = record.get(key)
            if _is_finite_number(value) and float(value) > 0.0:
                battery_samples.append(float(value))
    battery_available = (
        _is_finite_number(battery_before)
        and float(battery_before) > 0.0
        and _is_finite_number(battery_after)
        and float(battery_after) > 0.0
        and bool(battery_samples)
    )
    battery_peak = max(battery_samples) if battery_available else math.inf
    battery_rise = battery_peak - float(battery_before) if battery_available else math.inf

    ttft_values = [
        float(record["time_to_first_text_ms"])
        for record in case_records
        if _is_finite_number(record.get("time_to_first_text_ms"))
        and float(record["time_to_first_text_ms"]) >= 0.0
    ]
    mean_ttft_ms = statistics.fmean(ttft_values) if ttft_values else math.inf

    policy = config["selection_policy"]
    rejection_reasons: list[str] = []
    if completed_cases != policy["required_completed_cases"]:
        rejection_reasons.append(
            f"completed {completed_cases}/{policy['required_completed_cases']} required cases"
        )
    if schema_reliability < policy["minimum_schema_reliability"]:
        rejection_reasons.append(
            f"schema reliability {schema_reliability:.3f} is below "
            f"{policy['minimum_schema_reliability']:.3f}"
        )
    if semantic_quality < policy["minimum_semantic_quality_score"]:
        rejection_reasons.append(
            f"semantic quality {semantic_quality:.2f} is below "
            f"{policy['minimum_semantic_quality_score']:.2f}"
        )
    if mean_decode_rate < policy["minimum_mean_decode_tokens_per_second"]:
        rejection_reasons.append(
            f"mean decode rate {mean_decode_rate:.2f} tok/s is below "
            f"{policy['minimum_mean_decode_tokens_per_second']:.2f} tok/s"
        )
    if peak_rss <= 0 or peak_rss > policy["maximum_peak_rss_bytes"]:
        rejection_reasons.append(
            f"peak RSS {peak_rss} is outside the allowed measured device budget"
        )
    if not battery_available:
        rejection_reasons.append("battery temperature was not measurable on the device")
    else:
        if battery_peak >= policy["maximum_battery_temperature_c"]:
            rejection_reasons.append(
                f"battery temperature {battery_peak:.1f} C reached the benchmark safety limit"
            )
        if battery_rise > policy["maximum_battery_temperature_rise_c"]:
            rejection_reasons.append(
                f"battery temperature rose {battery_rise:.1f} C, above the benchmark limit"
            )

    report = {
        "schema_version": 1,
        "benchmark_id": config["benchmark_id"],
        "candidate_id": candidate_id,
        "source_revision": candidate["source_revision"],
        "artifact_sha256": candidate["artifact"]["sha256"],
        "eligible": not rejection_reasons,
        "rejection_reasons": rejection_reasons,
        "measurements": {
            "load_time_ms": load_time_ms,
            "parameter_count": model_record.get("parameter_count"),
            "tensor_bytes": model_record.get("tensor_bytes"),
            "training_context_tokens": model_record.get("training_context_tokens"),
            "peak_rss_bytes": peak_rss,
            "schema_reliability": schema_reliability,
            "semantic_quality_score": semantic_quality,
            "mean_decode_tokens_per_second": mean_decode_rate,
            "mean_time_to_first_text_ms": mean_ttft_ms if math.isfinite(mean_ttft_ms) else None,
            "battery_temp_before_c": battery_before,
            "battery_temp_after_c": battery_after,
            "battery_peak_temp_c": battery_peak if battery_available else None,
            "battery_temperature_rise_c": battery_rise if battery_available else None,
            "thermal_zone_max_before_c": model_record.get("thermal_zone_max_before_c"),
            "thermal_zone_max_after_c": summary.get("thermal_zone_max_after_c"),
            "completed_cases": completed_cases,
        },
        "case_scores": case_scores,
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return report


def _ranking_key(report: dict[str, Any]) -> tuple[Any, ...]:
    measurements = report["measurements"]
    return (
        -float(measurements["semantic_quality_score"]),
        -float(measurements["schema_reliability"]),
        -float(measurements["mean_decode_tokens_per_second"]),
        int(measurements["peak_rss_bytes"]),
        float(measurements["load_time_ms"]),
        str(report["candidate_id"]),
    )


def select_candidate(
    *, config_path: Path, report_paths: list[Path], output_path: Path
) -> dict[str, Any]:
    config = _load_json(config_path)
    _validate_config(config)
    reports = [_load_json(path) for path in report_paths]
    expected_ids = {candidate["candidate_id"] for candidate in config["candidates"]}
    actual_ids = {report.get("candidate_id") for report in reports}
    if len(reports) != len(config["candidates"]) or actual_ids != expected_ids:
        raise ValueError("selection requires exactly one report for every frozen candidate")
    eligible = sorted(
        (report for report in reports if report.get("eligible") is True), key=_ranking_key
    )
    if not eligible:
        selection = {
            "schema_version": 1,
            "benchmark_id": config["benchmark_id"],
            "selected_candidate_id": None,
            "status": "no_candidate_passed",
            "candidate_reports": reports,
        }
        output_path.write_text(
            json.dumps(selection, indent=2, sort_keys=True) + "\n", encoding="utf-8"
        )
        return selection

    winner = eligible[0]
    selection = {
        "schema_version": 1,
        "benchmark_id": config["benchmark_id"],
        "selected_candidate_id": winner["candidate_id"],
        "status": "selected",
        "ranking_policy": config["selection_policy"]["ranking"],
        "eligible_candidate_ids": [report["candidate_id"] for report in eligible],
        "candidate_reports": reports,
    }
    output_path.write_text(json.dumps(selection, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return selection


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate_parser = subparsers.add_parser("validate")
    validate_parser.add_argument("--config", type=Path, required=True)

    score_parser = subparsers.add_parser("score")
    score_parser.add_argument("--config", type=Path, required=True)
    score_parser.add_argument("--cases", type=Path, required=True)
    score_parser.add_argument("--raw", type=Path, required=True)
    score_parser.add_argument("--candidate-id", required=True)
    score_parser.add_argument("--output", type=Path, required=True)

    select_parser = subparsers.add_parser("select")
    select_parser.add_argument("--config", type=Path, required=True)
    select_parser.add_argument("--report", action="append", type=Path, required=True)
    select_parser.add_argument("--output", type=Path, required=True)

    args = parser.parse_args()
    if args.command == "validate":
        config = _load_json(args.config)
        _validate_config(config)
        print(json.dumps({"benchmark_id": config["benchmark_id"], "status": "valid"}))
        return 0
    if args.command == "score":
        report = score_candidate(
            config_path=args.config,
            cases_path=args.cases,
            raw_path=args.raw,
            candidate_id=args.candidate_id,
            output_path=args.output,
        )
        print(json.dumps({"candidate_id": args.candidate_id, "eligible": report["eligible"]}))
        return 0

    selection = select_candidate(
        config_path=args.config, report_paths=args.report, output_path=args.output
    )
    print(
        json.dumps(
            {
                "status": selection["status"],
                "selected_candidate_id": selection["selected_candidate_id"],
            }
        )
    )
    return 0 if selection["status"] == "selected" else 1


if __name__ == "__main__":
    raise SystemExit(main())

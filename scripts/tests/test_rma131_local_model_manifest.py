#!/usr/bin/env python3
"""Deterministic RMA-131 JSON-manifest contract tests."""

from __future__ import annotations

import copy
import importlib.util
import json
from collections.abc import Callable
from pathlib import Path
from types import ModuleType
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = ROOT / "scripts/validate_local_llm_manifest.py"
FIXTURE_PATH = ROOT / "models/manifests/examples/rma131-synthetic-experimental.local-llm.json"
SELECTED_PATH = ROOT / "models/manifests/qwen3-0.6b-q4-k-m.local-llm.json"
V6_CONFIG_PATH = ROOT / "benchmarks/rma133/candidates-v6.json"
SCHEMA_PATH = ROOT / "models/manifests/local-llm-manifest.schema.json"


def load_validator() -> ModuleType:
    spec = importlib.util.spec_from_file_location("rma131_validator", VALIDATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Unable to load RMA-131 validator module.")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


VALIDATOR = load_validator()


def valid_document() -> dict[str, Any]:
    return json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))


def require_valid(document: dict[str, Any], label: str) -> None:
    errors = VALIDATOR.validate_manifest(document)
    if errors:
        raise AssertionError(f"{label}: expected valid manifest, found {errors}")


def require_invalid(
    mutate: Callable[[dict[str, Any]], None],
    expected_fragment: str,
    label: str,
) -> None:
    document = copy.deepcopy(valid_document())
    mutate(document)
    errors = VALIDATOR.validate_manifest(document)
    if not any(expected_fragment in error for error in errors):
        raise AssertionError(
            f"{label}: expected error containing {expected_fragment!r}, found {errors}"
        )


def test_schema_and_fixture_parse() -> None:
    schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
    fixture = valid_document()
    if schema["$schema"] != "https://json-schema.org/draft/2020-12/schema":
        raise AssertionError("RMA-131 schema must declare JSON Schema draft 2020-12.")
    if schema["properties"]["schema_version"]["const"] != 1:
        raise AssertionError("RMA-131 schema version must be exactly 1.")
    if fixture["identity"]["experimental"] is not True:
        raise AssertionError("Committed synthetic fixture must be explicitly experimental.")
    if "synthetic" not in fixture["identity"]["model_id"]:
        raise AssertionError("Committed fixture must not masquerade as a real candidate model.")
    require_valid(fixture, "synthetic fixture")


def test_structural_rejections() -> None:
    require_invalid(
        lambda doc: doc.__setitem__("schema_version", 2),
        "schema_version",
        "schema mismatch",
    )
    require_invalid(
        lambda doc: doc.__setitem__("unexpected", True),
        "unexpected keys",
        "unexpected root key",
    )
    require_invalid(
        lambda doc: doc["identity"].pop("license_id"),
        "missing keys",
        "missing identity field",
    )


def test_identity_rejections() -> None:
    require_invalid(
        lambda doc: doc["identity"].__setitem__("model_id", "Model/Bad"),
        "invalid identifier",
        "bad model id",
    )
    require_invalid(
        lambda doc: doc["identity"].__setitem__("source_uri", "http://example.invalid/model"),
        "absolute HTTPS",
        "non-https provenance",
    )
    require_invalid(
        lambda doc: doc["identity"].__setitem__(
            "source_uri", "https://user:secret@example.invalid/model"
        ),
        "without credentials",
        "credentialed provenance",
    )
    require_invalid(
        lambda doc: doc["identity"].__setitem__("experimental_reason", ""),
        "must not be empty",
        "experimental reason required",
    )

    def non_experimental_reason(doc: dict[str, Any]) -> None:
        doc["identity"]["experimental"] = False
        doc["identity"]["experimental_reason"] = "still marked experimental"

    require_invalid(
        non_experimental_reason,
        "require empty reason",
        "non-experimental contradiction",
    )


def test_runtime_and_artifact_rejections() -> None:
    require_invalid(
        lambda doc: doc["runtime"].__setitem__("runtime_id", "other_runtime"),
        "requires 'reachy_llama'",
        "wrong runtime",
    )
    require_invalid(
        lambda doc: doc["runtime"].__setitem__("requires_network_access", True),
        "local inference must be false",
        "network-required local runtime",
    )
    for path in (
        "/absolute/model.gguf",
        "../escape.gguf",
        "nested/../escape.gguf",
        "nested\\model.gguf",
        "C:/model.gguf",
        "model.GGUF",
    ):
        require_invalid(
            lambda doc, value=path: doc["artifact"].__setitem__("relative_path", value),
            "safe relative lowercase .gguf path",
            f"unsafe path {path}",
        )
    require_invalid(
        lambda doc: doc["artifact"].__setitem__("file_size_bytes", 0),
        "positive integer",
        "zero-size artifact",
    )
    require_invalid(
        lambda doc: doc["artifact"].__setitem__("sha256", "A" * 64),
        "64 lowercase hexadecimal",
        "uppercase hash",
    )


def test_gguf_and_inference_rejections() -> None:
    require_invalid(
        lambda doc: doc["gguf_metadata"].__setitem__("gguf_version", 0),
        "positive integer",
        "invalid gguf version",
    )
    require_invalid(
        lambda doc: doc["gguf_metadata"].__setitem__("parameter_count", 0),
        "positive integer",
        "invalid parameter count",
    )
    require_invalid(
        lambda doc: doc["inference"].__setitem__("chat_template", ""),
        "must not be empty",
        "empty chat template",
    )
    require_invalid(
        lambda doc: doc["inference"].__setitem__("stop_tokens", ["same", "same"]),
        "must be unique",
        "duplicate stop token",
    )
    require_invalid(
        lambda doc: doc["inference"].__setitem__("recommended_threads", 65),
        "1 through 64",
        "thread bound",
    )

    def context_mismatch(doc: dict[str, Any]) -> None:
        doc["inference"]["context_limit_tokens"] = 2048
        doc["inference"]["memory_estimate"]["basis_context_tokens"] = 4096

    require_invalid(
        context_mismatch,
        "exceeds context limit",
        "memory context exceeds model context",
    )

    def batch_mismatch(doc: dict[str, Any]) -> None:
        doc["inference"]["memory_estimate"]["basis_context_tokens"] = 1024
        doc["inference"]["memory_estimate"]["basis_batch_tokens"] = 2048

    require_invalid(
        batch_mismatch,
        "exceeds basis context",
        "memory batch exceeds basis context",
    )


def test_device_compatibility_rejections() -> None:
    require_invalid(
        lambda doc: doc["device_compatibility"].__setitem__("android_abis", ["x86_64"]),
        "requires arm64-v8a",
        "wrong Android ABI",
    )
    require_invalid(
        lambda doc: doc["device_compatibility"].__setitem__("minimum_android_api", 25),
        "at least 26",
        "API below native floor",
    )
    require_invalid(
        lambda doc: doc["device_compatibility"].__setitem__(
            "required_cpu_features", ["dotprod", "dotprod"]
        ),
        "must be unique",
        "duplicate CPU feature",
    )
    require_invalid(
        lambda doc: doc["device_compatibility"].__setitem__("reachy_llama_abi_version", 1),
        "must equal ABI 2",
        "runtime ABI mismatch",
    )
    require_invalid(
        lambda doc: doc["device_compatibility"].__setitem__("minimum_ram_bytes", 536870912),
        "smaller than peak-RAM estimate",
        "RAM compatibility understates estimate",
    )



def test_selected_manifest_matches_frozen_v6() -> None:
    selected = json.loads(SELECTED_PATH.read_text(encoding="utf-8"))
    config = json.loads(V6_CONFIG_PATH.read_text(encoding="utf-8"))
    require_valid(selected, "selected Qwen3 manifest")
    candidates = {item["candidate_id"]: item for item in config["candidates"]}
    candidate = candidates["qwen3-0.6b-q4-k-m"]
    if selected["identity"]["experimental"] is not False:
        raise AssertionError("RMA-133 selected manifest must not remain experimental.")
    if selected["identity"]["license_id"] != "Apache-2.0":
        raise AssertionError("Selected manifest license changed from the accepted candidate.")
    if selected["identity"]["source_revision"] != candidate["source_revision"]:
        raise AssertionError("Selected manifest source revision does not match V6.")
    if selected["artifact"]["file_size_bytes"] != candidate["artifact"]["file_size_bytes"]:
        raise AssertionError("Selected manifest artifact size does not match V6.")
    if selected["artifact"]["sha256"] != candidate["artifact"]["sha256"]:
        raise AssertionError("Selected manifest artifact hash does not match V6.")
    if selected["runtime"] != {"runtime_id": "reachy_llama", "abi_version": 2, "requires_network_access": False}:
        raise AssertionError("Selected manifest must require only local reachy_llama ABI 2.")
    if selected["device_compatibility"]["reachy_llama_abi_version"] != 2:
        raise AssertionError("Selected manifest device compatibility must require ABI 2.")
    inference = selected["inference"]
    if inference["context_limit_tokens"] != 40960:
        raise AssertionError("Selected manifest context must match measured Qwen3 metadata.")
    if inference["memory_estimate"] != {"peak_ram_bytes": 740380672, "basis_context_tokens": 2048, "basis_batch_tokens": 256}:
        raise AssertionError("Selected manifest memory profile must match V6 evidence.")
    if inference["recommended_threads"] != 4:
        raise AssertionError("Selected manifest thread recommendation must match V6.")
    if not inference["chat_template"].strip() or not inference["stop_tokens"]:
        raise AssertionError("Selected manifest must contain explicit tokenizer/chat metadata.")

def test_ui_has_no_candidate_model_ids() -> None:
    application_root = ROOT / "Assets/ReachyMini/Runtime/Application"
    settings_root = ROOT / "Assets/ReachyMini/Runtime/Core/Application"
    source = "\n".join(
        path.read_text(encoding="utf-8").lower()
        for root in (application_root, settings_root)
        for path in sorted(root.glob("*Settings*.cs"))
    )
    for prohibited in ("qwen", "gemma", "smollm", "phi-", "llama-"):
        if prohibited in source:
            raise AssertionError(f"UI/settings hard-code candidate model token {prohibited!r}.")


def main() -> int:
    tests = (
        test_schema_and_fixture_parse,
        test_structural_rejections,
        test_identity_rejections,
        test_runtime_and_artifact_rejections,
        test_gguf_and_inference_rejections,
        test_device_compatibility_rejections,
        test_selected_manifest_matches_frozen_v6,
        test_ui_has_no_candidate_model_ids,
    )
    for test in tests:
        test()
    print("RMA-131 local-model JSON manifest contracts passed (8 groups; synthetic + selected manifests).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

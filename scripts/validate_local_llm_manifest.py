#!/usr/bin/env python3
"""Validate Reachy Mini RMA-131 local LLM manifests without network access."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit

IDENTIFIER_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,127}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
SAFE_GGUF_PATH_RE = re.compile(
    r"^(?!/)(?!.*(?:^|/)\.{1,2}(?:/|$))(?!.*//)(?!.*\\)(?!.*:)[^/]+(?:/[^/]+)*\.gguf$"
)

ROOT_KEYS = {
    "schema_version",
    "identity",
    "runtime",
    "artifact",
    "gguf_metadata",
    "inference",
    "device_compatibility",
}
IDENTITY_KEYS = {
    "manifest_id",
    "model_id",
    "display_name",
    "model_version",
    "source_uri",
    "source_revision",
    "license_id",
    "experimental",
    "experimental_reason",
}
RUNTIME_KEYS = {"runtime_id", "abi_version", "requires_network_access"}
ARTIFACT_KEYS = {"relative_path", "file_size_bytes", "sha256"}
GGUF_KEYS = {
    "gguf_version",
    "architecture",
    "quantization",
    "parameter_count",
    "tokenizer_model",
    "tokenizer_pre",
}
INFERENCE_KEYS = {
    "context_limit_tokens",
    "chat_template",
    "stop_tokens",
    "memory_estimate",
    "recommended_threads",
}
MEMORY_KEYS = {"peak_ram_bytes", "basis_context_tokens", "basis_batch_tokens"}
COMPATIBILITY_KEYS = {
    "android_abis",
    "minimum_android_api",
    "required_cpu_features",
    "minimum_ram_bytes",
    "reachy_llama_abi_version",
}


def _is_int(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _require_exact_keys(value: Any, expected: set[str], path: str, errors: list[str]) -> None:
    if not isinstance(value, dict):
        errors.append(f"{path}: expected object")
        return
    actual = set(value)
    missing = sorted(expected - actual)
    extra = sorted(actual - expected)
    if missing:
        errors.append(f"{path}: missing keys {missing}")
    if extra:
        errors.append(f"{path}: unexpected keys {extra}")


def _require_text(
    value: object,
    path: str,
    errors: list[str],
    maximum: int,
    *,
    allow_empty: bool = False,
) -> None:
    if not isinstance(value, str):
        errors.append(f"{path}: expected string")
        return
    if not allow_empty and not value.strip():
        errors.append(f"{path}: must not be empty or whitespace")
    if len(value) > maximum:
        errors.append(f"{path}: exceeds {maximum} characters")


def _require_positive_int(value: object, path: str, errors: list[str]) -> None:
    if not _is_int(value) or value <= 0:
        errors.append(f"{path}: expected positive integer")


def _require_identifier(value: object, path: str, errors: list[str]) -> None:
    if not isinstance(value, str) or IDENTIFIER_RE.fullmatch(value) is None:
        errors.append(f"{path}: invalid identifier")


def _validate_identity(identity: Any, errors: list[str]) -> None:
    _require_exact_keys(identity, IDENTITY_KEYS, "identity", errors)
    if not isinstance(identity, dict):
        return

    _require_identifier(identity.get("manifest_id"), "identity.manifest_id", errors)
    _require_identifier(identity.get("model_id"), "identity.model_id", errors)
    for key in ("display_name", "model_version", "source_revision", "license_id"):
        _require_text(identity.get(key), f"identity.{key}", errors, 128)

    source_uri = identity.get("source_uri")
    _require_text(source_uri, "identity.source_uri", errors, 2048)
    if isinstance(source_uri, str):
        parsed = urlsplit(source_uri)
        if (
            parsed.scheme.lower() != "https"
            or not parsed.netloc
            or parsed.username is not None
            or parsed.password is not None
            or bool(parsed.fragment)
        ):
            errors.append(
                "identity.source_uri: must be absolute HTTPS without credentials or fragment"
            )

    experimental = identity.get("experimental")
    if not isinstance(experimental, bool):
        errors.append("identity.experimental: expected boolean")
    reason = identity.get("experimental_reason")
    if experimental is True:
        _require_text(reason, "identity.experimental_reason", errors, 512)
    elif experimental is False:
        if reason != "":
            errors.append(
                "identity.experimental_reason: non-experimental manifests require empty reason"
            )


def _validate_runtime(runtime: Any, errors: list[str]) -> None:
    _require_exact_keys(runtime, RUNTIME_KEYS, "runtime", errors)
    if not isinstance(runtime, dict):
        return
    if runtime.get("runtime_id") != "reachy_llama":
        errors.append("runtime.runtime_id: schema version 1 requires 'reachy_llama'")
    if runtime.get("abi_version") != 1:
        errors.append("runtime.abi_version: schema version 1 requires ABI 1")
    if runtime.get("requires_network_access") is not False:
        errors.append("runtime.requires_network_access: local inference must be false")


def _validate_artifact(artifact: Any, errors: list[str]) -> None:
    _require_exact_keys(artifact, ARTIFACT_KEYS, "artifact", errors)
    if not isinstance(artifact, dict):
        return
    path = artifact.get("relative_path")
    if not isinstance(path, str) or len(path) > 240 or SAFE_GGUF_PATH_RE.fullmatch(path) is None:
        errors.append("artifact.relative_path: expected safe relative lowercase .gguf path")
    _require_positive_int(artifact.get("file_size_bytes"), "artifact.file_size_bytes", errors)
    sha256 = artifact.get("sha256")
    if not isinstance(sha256, str) or SHA256_RE.fullmatch(sha256) is None:
        errors.append("artifact.sha256: expected 64 lowercase hexadecimal characters")


def _validate_gguf(metadata: Any, errors: list[str]) -> None:
    _require_exact_keys(metadata, GGUF_KEYS, "gguf_metadata", errors)
    if not isinstance(metadata, dict):
        return
    _require_positive_int(metadata.get("gguf_version"), "gguf_metadata.gguf_version", errors)
    _require_positive_int(metadata.get("parameter_count"), "gguf_metadata.parameter_count", errors)
    for key in ("architecture", "quantization", "tokenizer_model", "tokenizer_pre"):
        _require_text(metadata.get(key), f"gguf_metadata.{key}", errors, 128)


def _validate_inference(inference: Any, errors: list[str]) -> None:
    _require_exact_keys(inference, INFERENCE_KEYS, "inference", errors)
    if not isinstance(inference, dict):
        return

    context_limit = inference.get("context_limit_tokens")
    _require_positive_int(context_limit, "inference.context_limit_tokens", errors)
    _require_text(inference.get("chat_template"), "inference.chat_template", errors, 65536)

    stop_tokens = inference.get("stop_tokens")
    if not isinstance(stop_tokens, list):
        errors.append("inference.stop_tokens: expected array")
    else:
        if len(stop_tokens) > 32:
            errors.append("inference.stop_tokens: maximum 32 entries")
        if len(stop_tokens) != len({item for item in stop_tokens if isinstance(item, str)}):
            errors.append("inference.stop_tokens: entries must be unique")
        for index, token in enumerate(stop_tokens):
            _require_text(token, f"inference.stop_tokens[{index}]", errors, 256)

    memory = inference.get("memory_estimate")
    _require_exact_keys(memory, MEMORY_KEYS, "inference.memory_estimate", errors)
    if isinstance(memory, dict):
        peak_ram = memory.get("peak_ram_bytes")
        basis_context = memory.get("basis_context_tokens")
        basis_batch = memory.get("basis_batch_tokens")
        _require_positive_int(peak_ram, "inference.memory_estimate.peak_ram_bytes", errors)
        _require_positive_int(
            basis_context,
            "inference.memory_estimate.basis_context_tokens",
            errors,
        )
        _require_positive_int(
            basis_batch,
            "inference.memory_estimate.basis_batch_tokens",
            errors,
        )
        if _is_int(basis_context) and _is_int(context_limit) and basis_context > context_limit:
            errors.append(
                "inference.memory_estimate.basis_context_tokens: exceeds context limit"
            )
        if _is_int(basis_batch) and _is_int(basis_context) and basis_batch > basis_context:
            errors.append(
                "inference.memory_estimate.basis_batch_tokens: exceeds basis context"
            )

    threads = inference.get("recommended_threads")
    if not _is_int(threads) or not 1 <= threads <= 64:
        errors.append("inference.recommended_threads: expected integer from 1 through 64")


def _validate_compatibility(
    compatibility: Any,
    inference: Any,
    errors: list[str],
) -> None:
    _require_exact_keys(compatibility, COMPATIBILITY_KEYS, "device_compatibility", errors)
    if not isinstance(compatibility, dict):
        return

    if compatibility.get("android_abis") != ["arm64-v8a"]:
        errors.append("device_compatibility.android_abis: schema version 1 requires arm64-v8a")
    minimum_api = compatibility.get("minimum_android_api")
    if not _is_int(minimum_api) or minimum_api < 26:
        errors.append("device_compatibility.minimum_android_api: must be at least 26")

    features = compatibility.get("required_cpu_features")
    if not isinstance(features, list):
        errors.append("device_compatibility.required_cpu_features: expected array")
    else:
        if len(features) > 32:
            errors.append("device_compatibility.required_cpu_features: maximum 32 entries")
        if len(features) != len({item for item in features if isinstance(item, str)}):
            errors.append("device_compatibility.required_cpu_features: entries must be unique")
        for index, feature in enumerate(features):
            _require_identifier(
                feature,
                f"device_compatibility.required_cpu_features[{index}]",
                errors,
            )

    minimum_ram = compatibility.get("minimum_ram_bytes")
    _require_positive_int(minimum_ram, "device_compatibility.minimum_ram_bytes", errors)
    if compatibility.get("reachy_llama_abi_version") != 1:
        errors.append("device_compatibility.reachy_llama_abi_version: must equal ABI 1")

    if isinstance(inference, dict):
        memory = inference.get("memory_estimate")
        if isinstance(memory, dict):
            peak_ram = memory.get("peak_ram_bytes")
            if _is_int(minimum_ram) and _is_int(peak_ram) and minimum_ram < peak_ram:
                errors.append(
                    "device_compatibility.minimum_ram_bytes: smaller than peak-RAM estimate"
                )


def validate_manifest(document: Any) -> list[str]:
    """Return deterministic validation errors for one parsed manifest."""
    errors: list[str] = []
    _require_exact_keys(document, ROOT_KEYS, "$", errors)
    if not isinstance(document, dict):
        return errors
    if document.get("schema_version") != 1:
        errors.append("schema_version: expected 1")

    _validate_identity(document.get("identity"), errors)
    _validate_runtime(document.get("runtime"), errors)
    _validate_artifact(document.get("artifact"), errors)
    _validate_gguf(document.get("gguf_metadata"), errors)
    inference = document.get("inference")
    _validate_inference(inference, errors)
    _validate_compatibility(document.get("device_compatibility"), inference, errors)
    return errors


def validate_file(path: Path) -> list[str]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        return [f"{path}: cannot read valid UTF-8 JSON: {type(exc).__name__}: {exc}"]
    return validate_manifest(document)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", nargs="+", type=Path)
    args = parser.parse_args()

    failed = False
    for path in args.manifest:
        errors = validate_file(path)
        if errors:
            failed = True
            print(f"{path}: invalid local LLM manifest", file=sys.stderr)
            for error in errors:
                print(f"  - {error}", file=sys.stderr)
        else:
            print(f"{path}: valid RMA-131 local LLM manifest")
    return 2 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())

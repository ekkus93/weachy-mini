"""RMA-074 approval signing: canonical hashing and Ed25519 sign/verify via openssl."""

from __future__ import annotations

import copy
import hashlib
import importlib.util
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_profile_approval_validation (for
# `ApprovalValidationError`, `_require_dict`, and `canonical_json_bytes`). It
# is loaded either as part of the calibration_profile_approval.py facade's
# ordered bootstrap (in which case the sibling is already in sys.modules) or
# standalone / directly by path, in which case scripts/ is not necessarily on
# sys.path. To be self-sufficient in both cases, check sys.modules first and
# only fall back to loading the sibling by a path relative to this file if it
# isn't already registered.
if "calibration_profile_approval_validation" in sys.modules:
    calibration_profile_approval_validation = sys.modules["calibration_profile_approval_validation"]
else:
    _validation_spec = importlib.util.spec_from_file_location(
        "calibration_profile_approval_validation",
        Path(__file__).with_name("calibration_profile_approval_validation.py"),
    )
    if _validation_spec is None or _validation_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_profile_approval_validation.py")
    calibration_profile_approval_validation = importlib.util.module_from_spec(_validation_spec)
    sys.modules["calibration_profile_approval_validation"] = calibration_profile_approval_validation
    _validation_spec.loader.exec_module(calibration_profile_approval_validation)

ApprovalValidationError = calibration_profile_approval_validation.ApprovalValidationError
canonical_json_bytes = calibration_profile_approval_validation.canonical_json_bytes
_require_dict = calibration_profile_approval_validation._require_dict


def compute_approval_sha256(document: dict[str, Any]) -> str:
    candidate = copy.deepcopy(document)
    integrity = _require_dict(candidate.get("integrity"), "integrity")
    integrity.pop("approval_sha256", None)
    signature = _require_dict(candidate.get("signature"), "signature")
    signature.pop("signature_base64", None)
    return hashlib.sha256(canonical_json_bytes(candidate)).hexdigest()


def signature_payload_bytes(document: dict[str, Any]) -> bytes:
    candidate = copy.deepcopy(document)
    signature = _require_dict(candidate.get("signature"), "signature")
    signature.pop("signature_base64", None)
    return canonical_json_bytes(candidate)


def _openssl_sign(payload: bytes, private_key_path: Path) -> bytes:
    with tempfile.TemporaryDirectory() as temp_text:
        root = Path(temp_text)
        payload_path = root / "payload.bin"
        signature_path = root / "signature.bin"
        payload_path.write_bytes(payload)
        result = subprocess.run(
            [
                "openssl",
                "pkeyutl",
                "-sign",
                "-rawin",
                "-inkey",
                str(private_key_path),
                "-in",
                str(payload_path),
                "-out",
                str(signature_path),
            ],
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode != 0:
            raise ApprovalValidationError(
                f"OpenSSL Ed25519 signing failed: {result.stderr.strip()}"
            )
        return signature_path.read_bytes()


def _openssl_verify(payload: bytes, signature: bytes, public_key_path: Path) -> None:
    with tempfile.TemporaryDirectory() as temp_text:
        root = Path(temp_text)
        payload_path = root / "payload.bin"
        signature_path = root / "signature.bin"
        payload_path.write_bytes(payload)
        signature_path.write_bytes(signature)
        result = subprocess.run(
            [
                "openssl",
                "pkeyutl",
                "-verify",
                "-rawin",
                "-pubin",
                "-inkey",
                str(public_key_path),
                "-in",
                str(payload_path),
                "-sigfile",
                str(signature_path),
            ],
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode != 0:
            raise ApprovalValidationError("Ed25519 approval signature verification failed")

#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
from pathlib import Path

ROOT = Path.cwd().resolve()
MODULE_PATH = ROOT / "scripts/rma111_retry_applicator.py"
SPEC = importlib.util.spec_from_file_location("rma111_retry_applicator", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise SystemExit("Could not load the RMA-111 retry applicator")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
ORIGINAL = MODULE.correct_payload_preconditions


def corrected_payload_preconditions() -> None:
    ORIGINAL()
    path = ROOT / "scripts/run_rma111_lightweight_tracking_acceptance_android.sh"
    source = path.read_text(encoding="utf-8")
    suppressed_pipeline = "            || true\n"
    count = source.count(suppressed_pipeline)
    if count != 1:
        raise SystemExit(f"Unexpected RMA-111 ADB suppression count: {count}")
    path.write_text(source.replace(suppressed_pipeline, ""), encoding="utf-8")


MODULE.correct_payload_preconditions = corrected_payload_preconditions
Path(__file__).unlink()
MODULE.main()

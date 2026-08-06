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

    acceptance_path = (
        ROOT / "scripts/run_rma111_lightweight_tracking_acceptance_android.sh"
    )
    acceptance = acceptance_path.read_text(encoding="utf-8")
    suppressed_pipeline = "            || true\n"
    suppression_count = acceptance.count(suppressed_pipeline)
    if suppression_count != 1:
        raise SystemExit(
            f"Unexpected RMA-111 ADB suppression count: {suppression_count}"
        )
    acceptance_path.write_text(
        acceptance.replace(suppressed_pipeline, ""),
        encoding="utf-8",
    )

    tracker_path = (
        ROOT
        / "Assets/ReachyMini/Runtime/Core/Perception/ReachyLightweightTracking.cs"
    )
    tracker = tracker_path.read_text(encoding="utf-8")
    malformed_descriptor = """            Descriptor = new ProviderDescriptor(
                "weachy.mlkit.lightweight-tracker",
                instanceId,
                backend.BackendVersion,
                VisionProviderKind.LightweightTracker,
                VisionProviderLocation.OnDevice);
"""
    corrected_descriptor = """            Descriptor = new ProviderDescriptor(
                VisionProviderKind.LightweightTracker,
                "weachy.mlkit.lightweight-tracker",
                instanceId,
                "On-device lightweight tracker",
                backend.BackendVersion,
                VisionProviderLocation.OnDevice);
"""
    descriptor_count = tracker.count(malformed_descriptor)
    if descriptor_count != 1:
        raise SystemExit(
            "Unexpected RMA-111 ProviderDescriptor constructor count: "
            f"{descriptor_count}"
        )
    tracker_path.write_text(
        tracker.replace(malformed_descriptor, corrected_descriptor),
        encoding="utf-8",
    )


MODULE.correct_payload_preconditions = corrected_payload_preconditions
Path(__file__).unlink()
MODULE.main()

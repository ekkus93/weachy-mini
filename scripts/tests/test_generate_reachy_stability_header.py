"""Tests for the generated RMA-060 native stability contract."""

from __future__ import annotations

import copy
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROFILE_PATH = ROOT / "models" / "reachy-mini" / "upstream-baseline-stability.json"
HEADER_PATH = (
    ROOT
    / "native"
    / "reachy_sim"
    / "feasibility"
    / "reachy_stability_profile.generated.h"
)
SCRIPT_PATH = ROOT / "scripts" / "generate_reachy_stability_header.py"

spec = importlib.util.spec_from_file_location("reachy_stability_header", SCRIPT_PATH)
if spec is None or spec.loader is None:
    raise RuntimeError("Cannot load stability header generator")
generator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = generator
spec.loader.exec_module(generator)


class ReachyStabilityHeaderTests(unittest.TestCase):
    """Verify the profile/header identity and failure paths."""

    def setUp(self) -> None:
        self.profile = json.loads(PROFILE_PATH.read_text(encoding="utf-8"))

    def test_committed_header_is_exactly_regenerated(self) -> None:
        """The native runner contract must be byte-derived from the JSON profile."""
        profile, profile_sha256 = generator.read_profile(PROFILE_PATH)
        rendered = generator.render_header(profile, profile_sha256)
        self.assertEqual(HEADER_PATH.read_text(encoding="utf-8"), rendered)
        self.assertIn("REACHY_STABILITY_REQUIRED_ANDROID_CYCLES 45U", rendered)
        self.assertIn("REACHY_STABILITY_STEPS_PER_CYCLE UINT64_C(20000)", rendered)
        self.assertIn('"upstream_sleep_request"', rendered)
        self.assertIn("UINT32_C(102)", rendered)

    def test_gate_duration_must_equal_the_generated_schedule(self) -> None:
        """The long-run claim cannot drift from cycles, phases, steps, or timestep."""
        profile = copy.deepcopy(self.profile)
        profile["long_duration_gate"]["required_simulated_seconds"] = 1799.0
        with self.assertRaisesRegex(
            generator.StabilityProfileError,
            "does not match the schedule",
        ):
            generator.validate_profile(profile)

    def test_sub_realtime_android_gate_is_rejected(self) -> None:
        """The representative phone must sustain the 500 Hz solver on average."""
        profile = copy.deepcopy(self.profile)
        profile["long_duration_gate"]["minimum_solver_realtime_factor"] = 0.99
        with self.assertRaisesRegex(generator.StabilityProfileError, "at least 1"):
            generator.validate_profile(profile)

    def test_non_500_hz_profile_is_rejected(self) -> None:
        """The named baseline must remain exactly 500 Hz."""
        profile = copy.deepcopy(self.profile)
        profile["timestep_seconds"] = 0.004
        with self.assertRaisesRegex(generator.StabilityProfileError, "exactly 0.002"):
            generator.validate_profile(profile)

    def test_unknown_allowed_overrange_actuator_is_rejected(self) -> None:
        """No phase may hide an unbound range exceedance."""
        profile = copy.deepcopy(self.profile)
        profile["phases"][0]["allowed_out_of_range_actuators"] = ["missing"]
        with self.assertRaisesRegex(generator.StabilityProfileError, "is invalid"):
            generator.validate_profile(profile)

    def test_check_mode_detects_stale_header(self) -> None:
        """A stale generated file must fail rather than building a different schedule."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "stale.h"
            output.write_text("stale\n", encoding="utf-8")
            old_argv = sys.argv
            try:
                sys.argv = [
                    str(SCRIPT_PATH),
                    "--profile",
                    str(PROFILE_PATH),
                    "--output",
                    str(output),
                    "--check",
                ]
                self.assertEqual(1, generator.main())
            finally:
                sys.argv = old_argv


if __name__ == "__main__":
    unittest.main()

"""Tests for the RMA-060 upstream-baseline stability profile and helpers."""

from __future__ import annotations

import copy
import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROFILE_PATH = ROOT / "models" / "reachy-mini" / "upstream-baseline-stability.json"
SCRIPT_PATH = ROOT / "scripts" / "run_reachy_upstream_baseline_stability.py"

spec = importlib.util.spec_from_file_location("reachy_stability", SCRIPT_PATH)
if spec is None or spec.loader is None:
    raise RuntimeError("Cannot load stability runner module")
stability = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = stability
spec.loader.exec_module(stability)


class UpstreamBaselineStabilityTests(unittest.TestCase):
    """Verify source-independent stability policy and schedule semantics."""

    def setUp(self) -> None:
        self.profile = json.loads(PROFILE_PATH.read_text(encoding="utf-8"))

    def test_committed_profile_is_valid_and_complete(self) -> None:
        """The committed profile must cover every required stability category."""
        config = stability.validate_config(self.profile)
        self.assertEqual("upstream_baseline", config.raw["profile_id"])
        self.assertEqual(0.002, config.timestep_seconds)
        self.assertEqual(9, len(config.actuator_names))
        categories = {phase["category"] for phase in config.raw["phases"]}
        self.assertTrue(
            {
                "neutral",
                "sleep",
                "body_yaw_limit",
                "head_actuator_limit",
                "antenna_extreme",
            }.issubset(categories)
        )
        self.assertEqual(20, len(config.raw["phases"]))

    def test_only_upstream_sleep_declares_range_exceedances(self) -> None:
        """No limit command may be silently marked as an allowed overrange request."""
        phases = self.profile["phases"]
        overrange = {
            phase["name"]: phase["allowed_out_of_range_actuators"]
            for phase in phases
            if phase["allowed_out_of_range_actuators"]
        }
        self.assertEqual(
            {
                "upstream_sleep_request": [
                    "stewart_1",
                    "stewart_2",
                    "stewart_5",
                    "stewart_6",
                ]
            },
            overrange,
        )

    def test_minimum_jerk_is_bounded_monotonic_and_endpoint_exact(self) -> None:
        """The transition curve must not overshoot actuator targets."""
        values = [stability.minimum_jerk(index / 100.0) for index in range(101)]
        self.assertEqual(0.0, values[0])
        self.assertEqual(1.0, values[-1])
        self.assertTrue(all(0.0 <= value <= 1.0 for value in values))
        self.assertTrue(all(values[index] <= values[index + 1] for index in range(len(values) - 1)))
        self.assertEqual(0.0, stability.minimum_jerk(-1.0))
        self.assertEqual(1.0, stability.minimum_jerk(2.0))

    def test_non_500_hz_profile_is_rejected(self) -> None:
        """The named upstream baseline must not drift from its 500 Hz contract."""
        profile = copy.deepcopy(self.profile)
        profile["timestep_seconds"] = 0.004
        with self.assertRaisesRegex(stability.StabilityError, "exactly 0.002"):
            stability.validate_config(profile)

    def test_unexplained_allowed_overrange_name_is_rejected(self) -> None:
        """Allowed overrange declarations must name a real profile actuator."""
        profile = copy.deepcopy(self.profile)
        profile["phases"][0]["allowed_out_of_range_actuators"] = ["unknown_motor"]
        with self.assertRaisesRegex(stability.StabilityError, "is invalid"):
            stability.validate_config(profile)

    def test_required_category_removal_is_rejected(self) -> None:
        """The suite cannot omit the sleep or boundary cases."""
        profile = copy.deepcopy(self.profile)
        profile["phases"] = [phase for phase in profile["phases"] if phase["category"] != "sleep"]
        with self.assertRaisesRegex(stability.StabilityError, "missing categories"):
            stability.validate_config(profile)


if __name__ == "__main__":
    unittest.main()

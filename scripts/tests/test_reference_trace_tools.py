"""Tests for the RMA-042 shared scenario and trace comparison tools."""

from __future__ import annotations

import copy
import hashlib
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
SCENARIO = ROOT / "models" / "reachy-mini" / "reference-scenario.json"
TRACE_LOCK = ROOT / "models" / "reachy-mini" / "reference-trace-desktop.lock.json"
HEADER = ROOT / "native" / "reachy_sim" / "feasibility" / "reachy_reference_scenario.generated.h"
HEADER_GENERATOR = ROOT / "scripts" / "generate_reachy_reference_header.py"
COMPARATOR = ROOT / "scripts" / "compare_reachy_reference_trace.py"
LOCK_VALIDATOR = ROOT / "scripts" / "validate_reference_trace_lock.py"


class ReferenceTraceToolTests(unittest.TestCase):
    """Verify scenario generation, trace identity, and comparison failure semantics."""

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        self.scenario_raw = SCENARIO.read_bytes()
        self.scenario = json.loads(self.scenario_raw)

    def synthetic_trace(self, platform: str) -> dict[str, Any]:
        """Build a complete zero-valued trace with valid metadata."""
        counts = self.scenario["expected_counts"]
        bodies = [
            {
                "name": name,
                "position_metres": [0.0, 0.0, 0.0],
                "quaternion_wxyz": [1.0, 0.0, 0.0, 0.0],
            }
            for name in self.scenario["body_names"]
        ]
        checkpoints = []
        timestep = self.scenario["timestep_seconds"]
        for step in self.scenario["checkpoint_steps"]:
            checkpoints.append(
                {
                    "step": step,
                    "simulation_time": step * timestep,
                    "maximum_equality_residual": 0.0,
                    "warning_count": 0,
                    "qpos": [0.0] * counts["nq"],
                    "qvel": [0.0] * counts["nv"],
                    "bodies": copy.deepcopy(bodies),
                }
            )
        return {
            "schema_version": 1,
            "status": "ok",
            "platform": platform,
            "scenario_id": self.scenario["scenario_id"],
            "scenario_sha256": hashlib.sha256(self.scenario_raw).hexdigest(),
            "source_model_sha256": self.scenario["source"]["model_sha256"],
            "mujoco_version": self.scenario["source"]["mujoco_version"],
            "compiled_counts": counts,
            "checkpoints": checkpoints,
        }

    def write_json(self, name: str, value: dict[str, Any]) -> Path:
        """Write deterministic temporary JSON."""
        path = self.root / name
        path.write_text(
            json.dumps(value, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        return path

    def run_comparator(
        self,
        desktop: dict[str, Any],
        android: dict[str, Any],
    ) -> subprocess.CompletedProcess[str]:
        """Run the public comparator CLI."""
        desktop_path = self.write_json("desktop.json", desktop)
        android_path = self.write_json("android.json", android)
        output_path = self.root / "comparison.json"
        return subprocess.run(
            [
                "python3",
                str(COMPARATOR),
                "--scenario",
                str(SCENARIO),
                "--desktop",
                str(desktop_path),
                "--android",
                str(android_path),
                "--output",
                str(output_path),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )

    def run_lock_validator(self, lock_path: Path) -> subprocess.CompletedProcess[str]:
        """Run the compact desktop trace-lock validator."""
        return subprocess.run(
            [
                "python3",
                str(LOCK_VALIDATOR),
                "--scenario",
                str(SCENARIO),
                "--lock",
                str(lock_path),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )

    def valid_trace_pair(self) -> tuple[dict[str, Any], dict[str, Any]]:
        """Return correctly identified synthetic desktop and Android traces."""
        return (
            self.synthetic_trace("desktop_reference"),
            self.synthetic_trace("android_arm64_api26"),
        )

    def test_committed_native_header_is_current(self) -> None:
        """The C scenario must be generated from the committed JSON."""
        result = subprocess.run(
            [
                "python3",
                str(HEADER_GENERATOR),
                "--scenario",
                str(SCENARIO),
                "--output",
                str(HEADER),
                "--check",
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )
        self.assertEqual(0, result.returncode, result.stderr)

    def test_committed_trace_lock_is_strictly_valid(self) -> None:
        """The compact fixture lock must contain exact hexadecimal identities."""
        result = self.run_lock_validator(TRACE_LOCK)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_nonhexadecimal_trace_hash_fails_lock_validation(self) -> None:
        """A 64-character non-hash string must not satisfy fixture integrity."""
        lock = json.loads(TRACE_LOCK.read_text(encoding="utf-8"))
        lock["trace_sha256"] = "g" * 64
        lock_path = self.write_json("invalid-lock.json", lock)
        result = self.run_lock_validator(lock_path)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("lowercase hexadecimal", result.stderr)

    def test_identical_complete_traces_pass(self) -> None:
        """A complete numerically identical Android trace must pass."""
        desktop, android = self.valid_trace_pair()
        result = self.run_comparator(desktop, android)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_trace_platform_identity_is_required(self) -> None:
        """A swapped or generic platform label must fail visibly."""
        desktop, android = self.valid_trace_pair()
        android["platform"] = "native"
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Android platform mismatch", result.stderr)

    def test_identically_wrong_simulation_schedule_fails(self) -> None:
        """Two matching traces must still agree with the scenario clock."""
        desktop, android = self.valid_trace_pair()
        desktop["checkpoints"][3]["simulation_time"] += 0.001
        android["checkpoints"][3]["simulation_time"] += 0.001
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("desktop simulation_time", result.stderr)

    def test_nonfinite_simulation_time_fails(self) -> None:
        """NaN must not disappear through maximum-error aggregation."""
        desktop, android = self.valid_trace_pair()
        android["checkpoints"][1]["simulation_time"] = float("nan")
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Android simulation_time is not finite", result.stderr)

    def test_qpos_error_above_tolerance_fails_visibly(self) -> None:
        """A state mismatch must name the exceeded tolerance."""
        desktop, android = self.valid_trace_pair()
        android["checkpoints"][1]["qpos"][0] = self.scenario["tolerances"]["qpos_absolute"] * 2.0
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("qpos_absolute", result.stderr)

    def test_bounded_equality_residual_is_required_even_when_traces_match(self) -> None:
        """Matching traces cannot legitimize an unstable loop closure."""
        desktop, android = self.valid_trace_pair()
        residual = self.scenario["tolerances"]["maximum_equality_residual"] * 2.0
        desktop["checkpoints"][4]["maximum_equality_residual"] = residual
        android["checkpoints"][4]["maximum_equality_residual"] = residual
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("bounded-residual policy", result.stderr)

    def test_negative_equality_residual_fails(self) -> None:
        """Absolute residual evidence cannot be negative."""
        desktop, android = self.valid_trace_pair()
        android["checkpoints"][2]["maximum_equality_residual"] = -1.0e-9
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("must not be negative", result.stderr)

    def test_body_order_mismatch_fails(self) -> None:
        """Named transform order is part of the compact fixture contract."""
        desktop, android = self.valid_trace_pair()
        bodies = android["checkpoints"][5]["bodies"]
        bodies[0], bodies[1] = bodies[1], bodies[0]
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Body order/name mismatch", result.stderr)

    def test_quaternion_sign_equivalence_passes(self) -> None:
        """Equivalent q and -q representations must not fail comparison."""
        desktop, android = self.valid_trace_pair()
        android["checkpoints"][2]["bodies"][3]["quaternion_wxyz"] = [
            -1.0,
            0.0,
            0.0,
            0.0,
        ]
        result = self.run_comparator(desktop, android)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_nonunit_quaternion_fails_even_when_traces_match(self) -> None:
        """Matching malformed transforms must not satisfy coordinate validity."""
        desktop, android = self.valid_trace_pair()
        malformed = [2.0, 0.0, 0.0, 0.0]
        desktop["checkpoints"][2]["bodies"][3]["quaternion_wxyz"] = malformed
        android["checkpoints"][2]["bodies"][3]["quaternion_wxyz"] = malformed
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("norm error", result.stderr)

    def test_warning_count_must_be_an_explicit_integer(self) -> None:
        """JSON false must not be accepted as the integer warning count zero."""
        desktop, android = self.valid_trace_pair()
        android["checkpoints"][1]["warning_count"] = False
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("nonnegative integer", result.stderr)


if __name__ == "__main__":
    unittest.main()

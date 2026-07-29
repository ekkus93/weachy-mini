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
HEADER = (
    ROOT
    / "native"
    / "reachy_sim"
    / "feasibility"
    / "reachy_reference_scenario.generated.h"
)
HEADER_GENERATOR = ROOT / "scripts" / "generate_reachy_reference_header.py"
COMPARATOR = ROOT / "scripts" / "compare_reachy_reference_trace.py"


class ReferenceTraceToolTests(unittest.TestCase):
    """Verify scenario generation and comparison failure semantics."""

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

    def test_identical_complete_traces_pass(self) -> None:
        """A complete numerically identical Android trace must pass."""
        desktop = self.synthetic_trace("desktop_reference")
        android = self.synthetic_trace("android_arm64_api26")
        result = self.run_comparator(desktop, android)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_qpos_error_above_tolerance_fails_visibly(self) -> None:
        """A state mismatch must name the exceeded tolerance."""
        desktop = self.synthetic_trace("desktop_reference")
        android = self.synthetic_trace("android_arm64_api26")
        android["checkpoints"][1]["qpos"][0] = (
            self.scenario["tolerances"]["qpos_absolute"] * 2.0
        )
        result = self.run_comparator(desktop, android)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("qpos_absolute", result.stderr)

    def test_quaternion_sign_equivalence_passes(self) -> None:
        """Equivalent q and -q representations must not fail comparison."""
        desktop = self.synthetic_trace("desktop_reference")
        android = self.synthetic_trace("android_arm64_api26")
        android["checkpoints"][2]["bodies"][3]["quaternion_wxyz"] = [
            -1.0,
            0.0,
            0.0,
            0.0,
        ]
        result = self.run_comparator(desktop, android)
        self.assertEqual(0, result.returncode, result.stderr)


if __name__ == "__main__":
    unittest.main()

"""RMA-074 read-only physical preflight tests."""

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/probe_reachy_physical_unit.py"
spec = importlib.util.spec_from_file_location("probe_reachy_physical_unit", SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError("cannot load probe_reachy_physical_unit")
probe = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = probe
spec.loader.exec_module(probe)


class PhysicalPreflightTests(unittest.TestCase):
    def valid_status(self) -> dict:
        return {
            "robot_name": "reachy_mini",
            "state": "running",
            "wireless_version": False,
            "simulation_enabled": False,
            "mockup_sim_enabled": False,
            "version": "1.7.3",
            "backend_status": {
                "ready": True,
                "motor_control_mode": "disabled",
                "last_alive": 123.5,
                "control_loop_stats": {},
                "error": None,
            },
        }

    def valid_state(self) -> dict:
        return {
            "control_mode": "disabled",
            "head_pose": {
                "pose_matrix": [
                    1.0,
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    1.0,
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    1.0,
                    0.2,
                    0.0,
                    0.0,
                    0.0,
                    1.0,
                ]
            },
            "head_joints": [0.0] * 7,
            "body_yaw": 0.0,
            "antennas_position": [0.0, 0.0],
        }

    def test_real_ready_backend_is_accepted(self) -> None:
        summary = probe.validate_daemon_status(self.valid_status())
        self.assertTrue(summary["backend_ready"])
        self.assertFalse(summary["wireless_version"])

    def test_simulation_is_rejected(self) -> None:
        status = self.valid_status()
        status["simulation_enabled"] = True
        with self.assertRaisesRegex(probe.PreflightError, "simulation"):
            probe.validate_daemon_status(status)

    def test_unready_or_faulted_backend_is_rejected(self) -> None:
        status = self.valid_status()
        status["backend_status"]["ready"] = False
        with self.assertRaisesRegex(probe.PreflightError, "not ready"):
            probe.validate_daemon_status(status)
        status = self.valid_status()
        status["backend_status"]["error"] = "motor bus unavailable"
        with self.assertRaisesRegex(probe.PreflightError, "reports error"):
            probe.validate_daemon_status(status)

    def test_hardware_id_is_hashed_not_returned(self) -> None:
        digest = probe.validate_hardware_id({"hardware_id": "unit-secret-serial"})
        self.assertEqual(len(digest), 64)
        self.assertNotIn("unit-secret-serial", digest)

    def test_missing_hardware_id_is_rejected(self) -> None:
        with self.assertRaisesRegex(probe.PreflightError, "physical hardware"):
            probe.validate_hardware_id({"hardware_id": None})

    def test_full_state_requires_exact_joint_and_pose_shapes(self) -> None:
        summary = probe.validate_full_state(self.valid_state())
        self.assertEqual(summary["head_joint_count"], 7)
        self.assertEqual(summary["head_pose_element_count"], 16)
        state = self.valid_state()
        state["head_joints"] = [0.0] * 6
        with self.assertRaisesRegex(probe.PreflightError, "exactly 7"):
            probe.validate_full_state(state)

    def test_nonfinite_state_is_rejected(self) -> None:
        state = self.valid_state()
        state["body_yaw"] = float("nan")
        with self.assertRaisesRegex(probe.PreflightError, "finite"):
            probe.validate_full_state(state)

    def test_candidate_list_is_deduplicated(self) -> None:
        candidates = probe.candidate_list("127.0.0.1", 8000)
        self.assertEqual(
            [(entry.host, entry.port) for entry in candidates].count(("127.0.0.1", 8000)),
            1,
        )


if __name__ == "__main__":
    unittest.main()

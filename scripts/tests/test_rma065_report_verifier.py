from __future__ import annotations

import copy
import json
import tempfile
import unittest
from pathlib import Path

import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

from verify_rma065_reports import Rma065ReportError, verify_reports


class Rma065ReportVerifierTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        root = Path(self.temp.name)
        self.audit_path = root / "audit.json"
        self.validation_path = root / "validation.json"
        self.profile_path = root / "profile.json"
        self.profile = {
            "contact_parameters": {"maximum_penetration_metres": 0.008}
        }
        neutral = {
            "steps": 5000,
            "warning_count": 0,
            "finite_qpos": True,
            "finite_qvel": True,
            "maximum_contact_count": 0,
            "maximum_penetration_metres": 0.0,
        }
        contact = {
            "steps": 500,
            "warning_count": 0,
            "finite_qpos": True,
            "finite_qvel": True,
            "observed_contact": True,
            "maximum_normal_force_newtons": 1.0,
            "maximum_impulse_newton_seconds": 0.002,
            "maximum_penetration_metres": 0.0005,
        }
        self.audit = {
            "contract": "rma065_enhanced_collision_audit_v1",
            "neutral_audit": copy.deepcopy(neutral),
            "compiled_inventory": {
                "collision_geom_count": 25,
                "collision_body_count": 17,
                "limited_joint_count": 9,
            },
        }
        self.validation = {
            "contract": "rma065_collision_hard_stop_validation_v1",
            "status": "ok",
            "acceptance": {
                "contact_force_and_impulse_exposed": True,
                "hard_stops_contain_outward_motion": True,
                "hosted_complexity_within_budget": True,
                "representative_external_contact_stable": True,
                "representative_internal_contact_stable": True,
            },
            "source_neutral": copy.deepcopy(neutral),
            "enhanced_neutral": copy.deepcopy(neutral),
            "internal_contact_fixture": copy.deepcopy(contact),
            "external_contact_fixture": copy.deepcopy(contact),
            "hard_stop_trials": [
                {
                    "joint": "yaw_body",
                    "warning_count": 0,
                    "observed_limit_constraint": True,
                    "upper_limit": 2.0,
                    "maximum_position": 1.999,
                },
                {
                    "joint": "right_antenna",
                    "warning_count": 0,
                    "observed_limit_constraint": True,
                    "upper_limit": 3.12,
                    "maximum_position": 3.1195,
                },
            ],
            "hosted_p95_overhead_ratio": 0.1,
        }

    def tearDown(self) -> None:
        self.temp.cleanup()

    def write(self) -> None:
        self.audit_path.write_text(json.dumps(self.audit), encoding="utf-8")
        self.validation_path.write_text(json.dumps(self.validation), encoding="utf-8")
        self.profile_path.write_text(json.dumps(self.profile), encoding="utf-8")

    def verify(self) -> dict[str, object]:
        self.write()
        return verify_reports(
            self.audit_path,
            self.validation_path,
            self.profile_path,
            5000,
        )

    def test_valid_reports_pass(self) -> None:
        self.assertEqual(self.verify()["status"], "ok")

    def test_missing_neutral_audit_is_rejected(self) -> None:
        self.audit["neutral"] = self.audit.pop("neutral_audit")
        with self.assertRaises(Rma065ReportError):
            self.verify()

    def test_nonzero_warning_is_rejected(self) -> None:
        self.audit["neutral_audit"]["warning_count"] = 1
        with self.assertRaises(Rma065ReportError):
            self.verify()

    def test_false_acceptance_is_rejected(self) -> None:
        self.validation["acceptance"]["hard_stops_contain_outward_motion"] = False
        with self.assertRaises(Rma065ReportError):
            self.verify()

    def test_excessive_penetration_is_rejected(self) -> None:
        self.validation["external_contact_fixture"]["maximum_penetration_metres"] = 0.009
        with self.assertRaises(Rma065ReportError):
            self.verify()

    def test_unreported_hard_stop_is_rejected(self) -> None:
        self.validation["hard_stop_trials"][0]["observed_limit_constraint"] = False
        with self.assertRaises(Rma065ReportError):
            self.verify()


if __name__ == "__main__":
    unittest.main()

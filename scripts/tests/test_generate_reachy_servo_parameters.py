from __future__ import annotations

import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "scripts" / "generate_reachy_servo_parameters.py"
SPEC = importlib.util.spec_from_file_location("servo_generator", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)

PROFILE_PATH = ROOT / "models" / "reachy-mini" / "servo-model-parameters.json"
GENERATED_PATH = ROOT / "native" / "reachy_sim" / "src" / "reachy_servo_parameters.generated.hpp"


class ServoParameterContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.profile = json.loads(PROFILE_PATH.read_text(encoding="utf-8"))

    def test_committed_header_is_exactly_regenerated(self) -> None:
        MODULE.validate_contract(self.profile)
        self.assertEqual(
            MODULE.generate(self.profile),
            GENERATED_PATH.read_text(encoding="utf-8"),
        )

    def test_all_actuators_have_ordered_role_safe_bindings(self) -> None:
        MODULE.validate_contract(self.profile)
        bindings = self.profile["actuator_bindings"]
        self.assertEqual(
            [entry["actuator_name"] for entry in bindings],
            [name for name, _ in MODULE.ACTUATORS],
        )

    def test_missing_actuator_binding_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["actuator_bindings"].pop()
        with self.assertRaisesRegex(MODULE.ContractError, "nine actuators"):
            MODULE.validate_contract(profile)

    def test_cross_role_binding_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["actuator_bindings"][1]["parameter_set_id"] = "antenna_upstream_placeholder"
        with self.assertRaisesRegex(MODULE.ContractError, "crosses actuator roles"):
            MODULE.validate_contract(profile)

    def test_unknown_quality_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["parameter_sets"][0]["parameters"]["nominal_voltage_volts"]["quality"] = "guess"
        with self.assertRaisesRegex(MODULE.ContractError, "quality is invalid"):
            MODULE.validate_contract(profile)

    def test_null_manufacturer_estimate_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        scalar = profile["parameter_sets"][0]["parameters"]["nominal_voltage_volts"]
        scalar["quality"] = "manufacturer_estimate"
        with self.assertRaisesRegex(MODULE.ContractError, "null only"):
            MODULE.validate_contract(profile)

    def test_calibrated_set_requires_complete_calibrated_values(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["parameter_sets"][0]["overall_quality"] = "calibrated"
        with self.assertRaisesRegex(MODULE.ContractError, "calibrated"):
            MODULE.validate_contract(profile)

    def test_stale_header_is_detected(self) -> None:
        MODULE.validate_contract(self.profile)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "generated.hpp"
            output.write_text("stale\n", encoding="utf-8")
            self.assertNotEqual(
                MODULE.generate(self.profile),
                output.read_text(encoding="utf-8"),
            )


if __name__ == "__main__":
    unittest.main()

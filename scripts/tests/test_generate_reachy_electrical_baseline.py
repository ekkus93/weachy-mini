from __future__ import annotations

import copy
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GENERATOR_PATH = ROOT / "scripts" / "generate_reachy_electrical_baseline.py"
PROFILE_PATH = ROOT / "models" / "reachy-mini" / "electrical-controller-baseline.json"
HEADER_PATH = ROOT / "native" / "reachy_sim" / "src" / "reachy_electrical_baseline.generated.hpp"

spec = importlib.util.spec_from_file_location("rma062_generator", GENERATOR_PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class ElectricalBaselineContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.data = json.loads(PROFILE_PATH.read_text(encoding="utf-8"))

    def assert_rejected(self, data: dict) -> None:
        with self.assertRaises(module.ContractError):
            module.validate_contract(data)

    def test_committed_header_is_exactly_regenerated(self) -> None:
        module.validate_contract(self.data)
        self.assertEqual(HEADER_PATH.read_text(encoding="utf-8"), module.generate(self.data))

    def test_pinned_source_drift_is_rejected(self) -> None:
        changed = copy.deepcopy(self.data)
        changed["source"]["reachy_commit"] = "0" * 40
        self.assert_rejected(changed)

    def test_calibrated_claim_is_rejected(self) -> None:
        changed = copy.deepcopy(self.data)
        changed["baselines"][0]["overall_quality"] = "calibrated"
        self.assert_rejected(changed)

    def test_placeholder_or_null_value_is_rejected(self) -> None:
        changed = copy.deepcopy(self.data)
        scalar = changed["baselines"][1]["servo_parameters"]["stall_torque_newton_metres"]
        scalar["quality"] = "placeholder"
        scalar["value"] = None
        self.assert_rejected(changed)

    def test_unit_contract_drift_is_rejected(self) -> None:
        changed = copy.deepcopy(self.data)
        changed["unit_contract"]["servo_parameters"]["stall_torque_newton_metres"] = "Nmm"
        self.assert_rejected(changed)

    def test_encoder_conversion_drift_is_rejected(self) -> None:
        changed = copy.deepcopy(self.data)
        changed["baselines"][2]["servo_parameters"]["encoder_position_quantum_radians"]["value"] = (
            0.001
        )
        self.assert_rejected(changed)

    def test_cross_role_binding_is_rejected(self) -> None:
        changed = copy.deepcopy(self.data)
        changed["actuator_bindings"][0]["baseline_id"] = "stewart_xl330_m288_estimate"
        self.assert_rejected(changed)

    def test_stale_header_is_detected_by_cli(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "stale.hpp"
            output.write_text("stale\n", encoding="utf-8")
            old_argv = sys.argv
            try:
                sys.argv = [
                    str(GENERATOR_PATH),
                    "--profile",
                    str(PROFILE_PATH),
                    "--output",
                    str(output),
                    "--check",
                ]
                self.assertEqual(module.main(), 1)
            finally:
                sys.argv = old_argv


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import copy
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROFILE_PATH = ROOT / "models" / "reachy-mini" / "mechanical-effects-baseline.json"
HEADER_PATH = ROOT / "native" / "reachy_sim" / "src" / "reachy_mechanical_baseline.generated.hpp"
SCRIPT_PATH = ROOT / "scripts" / "generate_reachy_mechanical_baseline.py"

spec = importlib.util.spec_from_file_location("generate_reachy_mechanical_baseline", SCRIPT_PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class MechanicalBaselineContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.data = json.loads(PROFILE_PATH.read_text(encoding="utf-8"))

    def test_committed_header_is_exactly_regenerated(self) -> None:
        module.validate_contract(self.data)
        self.assertEqual(module.generate(self.data), HEADER_PATH.read_text(encoding="utf-8"))

    def test_calibrated_claim_is_rejected(self) -> None:
        altered = copy.deepcopy(self.data)
        altered["parameter_sets"][0]["overall_evidence_class"] = "calibrated"
        with self.assertRaisesRegex(module.ContractError, "invalid for RMA-063"):
            module.validate_contract(altered)

    def test_missing_evidence_is_rejected(self) -> None:
        altered = copy.deepcopy(self.data)
        altered["parameter_sets"][0]["parameters"]["coulomb_friction_newton_metres"]["evidence_id"] = ""
        with self.assertRaisesRegex(module.ContractError, "non-empty string"):
            module.validate_contract(altered)

    def test_invalid_stiction_order_is_rejected(self) -> None:
        altered = copy.deepcopy(self.data)
        parameters = altered["parameter_sets"][1]["parameters"]
        parameters["stiction_exit_velocity_radians_per_second"]["value"] = parameters[
            "stiction_enter_velocity_radians_per_second"
        ]["value"]
        with self.assertRaisesRegex(module.ContractError, "exit velocity must exceed"):
            module.validate_contract(altered)

    def test_cross_role_parameter_copy_is_rejected(self) -> None:
        altered = copy.deepcopy(self.data)
        altered["parameter_sets"][2]["parameters"] = copy.deepcopy(
            altered["parameter_sets"][1]["parameters"]
        )
        with self.assertRaisesRegex(module.ContractError, "cross-role mechanical parameter copy"):
            module.validate_contract(altered)

    def test_cross_role_binding_is_rejected(self) -> None:
        altered = copy.deepcopy(self.data)
        altered["actuator_bindings"][0]["parameter_set_id"] = altered["parameter_sets"][1]["id"]
        with self.assertRaisesRegex(module.ContractError, "crosses actuator roles"):
            module.validate_contract(altered)

    def test_unit_contract_drift_is_rejected(self) -> None:
        altered = copy.deepcopy(self.data)
        altered["unit_contract"]["backlash_half_width_radians"] = "degrees"
        with self.assertRaisesRegex(module.ContractError, "unit_contract differs"):
            module.validate_contract(altered)

    def test_stale_header_is_detected_by_cli(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "generated.hpp"
            output.write_text("stale\n", encoding="utf-8")
            result = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT_PATH),
                    "--profile",
                    str(PROFILE_PATH),
                    "--output",
                    str(output),
                    "--check",
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("stale", result.stdout)


if __name__ == "__main__":
    unittest.main()
